# -----------------------------------------------------------------------
#  Mud.AI Debug 打包脚本（仅供本地调试/排障使用）
#
#  本脚本只是 pack.ps1 的薄封装：
#    ① 输出到独立的 artifacts-debug 目录 —— 不得覆盖发布目录 artifacts
#       （Debug 程序集未做优化，混入发布目录会被当作发布包发出）；
#    ② 项目清单不在此处维护 —— 由 pack.ps1 从 Mud.AI.slnx 解析，
#       漏配会在 pack.ps1 的包集合校验处失败，而不是静默少发一个包。
#
#  打包成功后，默认把 artifacts-debug 中的包同版本覆盖到本机 NuGet 全局缓存
#  （global-packages），便于不发布到 NuGet 平台即可在引用方项目中调试最新功能；
#  -SkipCacheSync 可跳过该步骤。
#  注意：已还原过的引用方项目需 dotnet restore --force（或删除 obj）后重新构建，
#  才会真正用上新缓存内容。
#
#  用法: .\pack_debug.ps1 [版本号] [-SkipCacheSync] [-SyncOnly]
# -----------------------------------------------------------------------

param(
    [string]$Version = "",
    [switch]$SkipCacheSync,
    [switch]$SyncOnly
)

$ErrorActionPreference = "Stop"
# 本脚本位于 scripts/ 子目录，仓库根 = 脚本目录的父目录
$RootDir = Split-Path -Parent $PSScriptRoot

Write-Host "!!! Debug 打包：产物仅供本地调试，请勿发布到 NuGet !!!" -ForegroundColor Red
Write-Host ""

$DebugOutputDir = "artifacts-debug"
# 默认携带 -ForceSameVersion —— 本脚本的设计目的就是「同版本覆盖全局缓存做本地调试」
# （内容哈希 .nupkg.metadata 保证新鲜度），不得被 pack.ps1 的同版本重打防护拦住
$forward = @{ Configuration = "Debug"; OutputDir = $DebugOutputDir; ForceSameVersion = $true }
if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $forward.Version = $Version
}

if (-not $SyncOnly) {
    & (Join-Path $PSScriptRoot "pack.ps1") @forward
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

if ($SkipCacheSync) {
    Write-Host ""
    Write-Host "已按 -SkipCacheSync 跳过 NuGet 缓存同步。" -ForegroundColor DarkYellow
    exit 0
}

# -------------------------------------------------------------------------
#  同步 NuGet 全局缓存（global-packages）：按 包ID+版本 直接覆盖缓存目录。
#  布局对齐 NuGet 官方：<gp>/<id小写>/<规范化版本小写>/（解包内容 + nupkg 副本）
# -------------------------------------------------------------------------
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  同步 NuGet 全局缓存（同版本覆盖）" -ForegroundColor Cyan

# 解析全局缓存目录：NUGET_PACKAGES 环境变量优先，其次 dotnet 配置，最后默认路径
$GlobalPackages = $env:NUGET_PACKAGES
if ([string]::IsNullOrWhiteSpace($GlobalPackages)) {
    $localsOutput = (& dotnet nuget locals global-packages --list) -join "`n"
    if ($LASTEXITCODE -eq 0 -and $localsOutput -match 'global-packages:\s*(.+)') {
        $GlobalPackages = $Matches[1].Trim()
    } else {
        $GlobalPackages = Join-Path $env:USERPROFILE ".nuget\packages"
    }
}
Write-Host "  缓存目录: $GlobalPackages" -ForegroundColor DarkGray

# 对齐 NuGet 版本规范化：去构建元数据、去前导零、4 段版本末尾 .0 收敛为 3 段
function Get-NormalizedVersion {
    param([string]$v)
    $v = $v.Split('+')[0].ToLowerInvariant()
    $parts = $v -split '-'
    $numeric = New-Object System.Collections.ArrayList
    foreach ($p in ($parts[0] -split '\.')) {
        $n = 0
        if ([int]::TryParse($p, [ref]$n)) {
            [void]$numeric.Add([string]$n)
        }
        else {
            return $v
        }
    }
    while ($numeric.Count -gt 3 -and $numeric[-1] -eq '0') {
        $numeric = $numeric[0..($numeric.Count - 2)]
    }
    $prerelease = if ($parts.Count -gt 1) { '-' + ($parts[1..($parts.Count - 1)] -join '-') } else { '' }
    return ($numeric -join '.') + $prerelease
}

# 手写解压：PS 5.1 下 ZipArchive 实例上解析不到扩展方法 ExtractToDirectory，
# 故逐条目流拷贝，仅依赖实例方法 Open/CopyTo
function Expand-Nupkg {
    param(
        [string]$ZipPath,
        [string]$DestDir
    )
    $zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        foreach ($entry in $zip.Entries) {
            # 目录占位条目（以 / 结尾）只建目录，不写文件
            if ($entry.FullName.EndsWith('/') -or $entry.FullName.EndsWith('\')) {
                continue
            }
            $targetPath = Join-Path $DestDir ($entry.FullName -replace '/', '\')
            $entryDir = Split-Path -Parent $targetPath
            if ($entryDir -and -not (Test-Path $entryDir)) {
                New-Item -ItemType Directory -Path $entryDir -Force | Out-Null
            }
            $src = $entry.Open()
            try {
                $dst = [System.IO.File]::Create($targetPath)
                try {
                    $src.CopyTo($dst)
                } finally {
                    $dst.Dispose()
                }
            } finally {
                $src.Dispose()
            }
        }
    } finally {
        $zip.Dispose()
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$ArtifactsDir = Join-Path $RootDir $DebugOutputDir
$nupkgs = @(Get-ChildItem $ArtifactsDir -Filter "*.nupkg" -File -ErrorAction SilentlyContinue)
if ($nupkgs.Count -eq 0) {
    Write-Host "  [FAIL] $ArtifactsDir 中没有找到 nupkg，请先完整打包一次（不带 -SyncOnly）。" -ForegroundColor Red
    exit 1
}
# 包 ID 可含点、版本以数字开头：用非贪婪 ID + 数字开头的版本来切分文件名
$pkgNameRegex = '^(?<id>.+?)\.(?<version>\d[\w\.\-\+]*)\.nupkg$'

$syncedCount = 0
$failedCount = 0

foreach ($pkg in $nupkgs) {
    if ($pkg.Name -notmatch $pkgNameRegex) {
        Write-Host "  [FAIL] 无法从文件名解析包 ID/版本: $($pkg.Name)" -ForegroundColor Red
        $failedCount++
        continue
    }

    $id = $Matches['id']
    $ver = Get-NormalizedVersion $Matches['version']
    # 缓存目录统一小写（与 NuGet 官方布局一致）
    $targetDir = Join-Path $GlobalPackages ("{0}\{1}" -f $id.ToLowerInvariant(), $ver)

    try {
        # 先删后建：既清掉陈旧文件，也避开旧版 PowerShell 不支持 zip 覆盖解压的问题
        if (Test-Path $targetDir) {
            try {
                Remove-Item $targetDir -Recurse -Force -ErrorAction Stop
            } catch {
                # 部分环境（安全删除/回收站拦截、文件被占用）下 Remove-Item 会失败；
                # 退回 .NET 原生递归删除，避免「同版本覆盖静默失败 ⇒ 引用方仍加载旧包」
                [System.IO.Directory]::Delete($targetDir, $true)
            }
        }
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null

        Expand-Nupkg -ZipPath $pkg.FullName -DestDir $targetDir

        # 补齐 NuGet 官方缓存布局：包根目录下要有 <id>.<ver>.nupkg 副本与小写 nuspec
        Copy-Item $pkg.FullName (Join-Path $targetDir ("{0}.{1}.nupkg" -f $id.ToLowerInvariant(), $ver)) -Force
        Get-ChildItem $targetDir -Filter "*.nuspec" -File | ForEach-Object {
            $lowerNuspec = "$($id.ToLowerInvariant()).nuspec"
            if ($_.Name -cne $lowerNuspec) {
                Rename-Item $_.FullName $lowerNuspec -Force
            }
        }

        # 写入 NuGet 缓存元数据（.nupkg.metadata + .sha512）。
        # 缺少 .nupkg.metadata 时 NuGet 视该包为「未完成安装」，restore 会从线上源
        # 重新下载同版本包并覆盖缓存 —— 表现为「本地重打包后引用方仍使用线上旧产物」。
        $sha = [System.Security.Cryptography.SHA512]::Create()
        $fs = [System.IO.File]::OpenRead($pkg.FullName)
        try {
            $contentHash = [Convert]::ToBase64String($sha.ComputeHash($fs))
        } finally {
            $fs.Dispose()
            $sha.Dispose()
        }
        Set-Content (Join-Path $targetDir ("{0}.{1}.nupkg.sha512" -f $id.ToLowerInvariant(), $ver)) $contentHash -NoNewline -Encoding ascii
        # 路径统一为正斜杠，避免 Windows 反斜杠在 JSON 中成为非法转义序列（NuGet 解析会直接报错）
        $metadataJson = ([ordered]@{
                version     = 2
                contentHash = $contentHash
                source      = $ArtifactsDir.Replace('\', '/')
            } | ConvertTo-Json -Compress)
        [System.IO.File]::WriteAllText((Join-Path $targetDir ".nupkg.metadata"), $metadataJson, (New-Object System.Text.UTF8Encoding($false)))

        Write-Host "  [ OK ] $id $ver → $targetDir" -ForegroundColor Green
        $syncedCount++
    } catch {
        Write-Host "  [FAIL] $id $ver ：$($_.Exception.Message)" -ForegroundColor Red
        Write-Host "         请参考上方异常排查（常见原因：目录被占用、权限不足）。" -ForegroundColor DarkYellow
        $failedCount++
    }
}

Write-Host "========================================" -ForegroundColor Cyan

if ($failedCount -gt 0) {
    Write-Host "  缓存同步失败 $failedCount 个（成功 $syncedCount 个）" -ForegroundColor Red
    exit 1
}
Write-Host "  已同步 $syncedCount 个包到 NuGet 全局缓存" -ForegroundColor Green
Write-Host "  提示：已还原过的项目需 dotnet restore --force（或删除 obj）再构建，才会用上新缓存。" -ForegroundColor DarkYellow
exit 0
