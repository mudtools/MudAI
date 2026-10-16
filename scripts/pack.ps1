# -----------------------------------------------------------------------
#  Mud.AI 打包脚本
#  用法: .\pack.ps1 [配置] [版本号] [输出目录]
#  示例: .\pack.ps1 Release 1.0.2
#        .\pack.ps1 Debug   1.0.2 artifacts-debug   # Debug 包必须落到独立目录
#
#  打包清单自动取自 Mud.AI.slnx 的 /Sources/ 段（Tests/** 为 IsPackable=false，
#  不产出包）；pack_debug.ps1 只是本脚本的薄封装，不各自维护清单。
#
#  安全约束：
#    - 同版本重打包默认被拒绝：NuGet 全局缓存按 id+version 键控，重打不会失效
#      下游缓存，下游会继续消费旧产物。确需覆盖时用 -ForceSameVersion；
#    - 非 Release 配置必须配独立输出目录，Debug 程序集不得充当发布包；
#    - 打包前只构建一次、pack 全程 --no-build，校验基准取该次构建产物快照，
#      包内 DLL 必须与快照字节一致（防错版/陈旧打包）。
# -----------------------------------------------------------------------

param(
    [string]$Configuration = "Release",
    [string]$Version = "",
    [string]$OutputDir = "artifacts",
    [switch]$ForceSameVersion
)

$ErrorActionPreference = "Stop"
# 本脚本位于 scripts/ 子目录，仓库根 = 脚本目录的父目录
$RootDir = Split-Path -Parent $PSScriptRoot
$Solution = Join-Path $RootDir "Mud.AI.slnx"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Mud.AI 打包脚本" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# 保留 Directory.Build.props 原有编码（无 BOM 则写回无 BOM），避免编码漂移产生无关 diff
function Set-PropsVersion {
    param(
        [string]$PropsPath,
        [string]$NewVersion
    )
    $bytes = [System.IO.File]::ReadAllBytes($PropsPath)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    $text = [System.IO.File]::ReadAllText($PropsPath, [System.Text.Encoding]::UTF8)
    if ($text -notmatch '<Version>[^<]*</Version>') {
        throw "Directory.Build.props 中未找到 <Version> 节点: $PropsPath"
    }
    $text = $text -replace '<Version>[^<]*</Version>', ("<Version>" + $NewVersion + "</Version>")
    [System.IO.File]::WriteAllText($PropsPath, $text, (New-Object System.Text.UTF8Encoding($hasBom)))
}

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    Write-Host "更新版本号: $Version" -ForegroundColor Yellow
    $propsPath = Join-Path $RootDir "Directory.Build.props"
    if (-not (Test-Path $propsPath)) {
        Write-Host "未找到 Directory.Build.props: $propsPath" -ForegroundColor Red
        exit 1
    }
    try {
        Set-PropsVersion -PropsPath $propsPath -NewVersion $Version
    }
    catch {
        Write-Host "更新版本号失败: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
    Write-Host "版本号已更新" -ForegroundColor Green
    Write-Host ""
}

# 打包清单：.slnx /Sources/ 段的工程（Tests/** 为 IsPackable=false，不产出包）
$Projects = @()
if (Test-Path $Solution) {
    $slnxRaw = [System.IO.File]::ReadAllText($Solution)
    foreach ($m in [regex]::Matches($slnxRaw, '<Project Path="([^"]+\.csproj)"')) {
        $rel = ($m.Groups[1].Value -replace '/', '\')
        if ($rel -like 'Tests\*') { continue }
        if ($Projects -notcontains $rel) { $Projects += $rel }
    }
}
if ($Projects.Count -eq 0) {
    Write-Host "未能从 Mud.AI.slnx 解析出可打包工程（/Sources/ 段）" -ForegroundColor Red
    exit 1
}
# 包 ID = 工程文件名去扩展名；包集合校验与缓存碰撞探测都以它为基准
$ExpectedPackages = @($Projects | ForEach-Object { (Split-Path $_ -Leaf) -replace '\.csproj$', '' })
Write-Host "打包清单（$($Projects.Count) 个工程）: $($ExpectedPackages -join ', ')" -ForegroundColor DarkGray
Write-Host ""

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

$targetVersion = $Version
if ([string]::IsNullOrWhiteSpace($targetVersion)) {
    $propsRaw = [System.IO.File]::ReadAllText((Join-Path $RootDir "Directory.Build.props"))
    if ($propsRaw -match '<Version>([^<]+)</Version>') {
        $targetVersion = $Matches[1].Trim()
    }
}

if (-not [string]::IsNullOrWhiteSpace($targetVersion)) {
    # 版本纪律告警（仅警告，不阻断）：目标版本不高于 CHANGELOG 最新发布版本时提示
    $changelogPath = Join-Path $RootDir "CHANGELOG.md"
    if (Test-Path $changelogPath) {
        $latestPublished = $null
        foreach ($line in [System.IO.File]::ReadAllLines($changelogPath)) {
            if ($line -match '^##\s+\[(\d+\.\d+\.\d+)\]') {
                $latestPublished = $Matches[1]
                break   # 版本段自新到旧排列，首个数字版本号即最新发布版本
            }
        }
        if ($latestPublished) {
            try {
                if ([version]$targetVersion -le [version]$latestPublished) {
                    Write-Host "!!! 版本纪律告警：目标版本 $targetVersion 不高于 CHANGELOG 最新发布版本 $latestPublished。" -ForegroundColor Yellow
                    Write-Host "    同版本重打包不会使下游 NuGet 缓存失效，请确认这是有意为之（否则请递增版本号）。" -ForegroundColor Yellow
                    Write-Host ""
                }
            }
            catch {
                # 预发布版本（如 1.0.2-beta.1）无法按 [version] 比较，跳过该告警
            }
        }
    }

    # 版本递增强制校验（在清理 artifacts / 还原 / 打包之前执行）：
    #   对每个预期包探测全局缓存，同版本已存在即拒绝打包（-ForceSameVersion 逃生）。
    $normalizedTarget = Get-NormalizedVersion $targetVersion

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

    $cacheCollisions = @()
    foreach ($id in $ExpectedPackages) {
        $cacheDir = Join-Path $GlobalPackages ("{0}\{1}" -f $id.ToLowerInvariant(), $normalizedTarget)
        if (Test-Path $cacheDir) {
            $cacheCollisions += "$id $normalizedTarget"
        }
    }

    if ($cacheCollisions.Count -gt 0) {
        if (-not $ForceSameVersion) {
            Write-Host "========================================" -ForegroundColor Red
            Write-Host "  打包被拒绝（版本递增强制校验）：" -ForegroundColor Red
            Write-Host "  目标版本 $normalizedTarget 已存在于 NuGet 全局缓存：" -ForegroundColor Red
            foreach ($c in $cacheCollisions) {
                Write-Host "    - $c" -ForegroundColor Red
            }
            Write-Host ""
            Write-Host "  同版本重打包不会使下游缓存失效（NuGet 按 id+version 键控），将导致下游继续使用旧产物。" -ForegroundColor Yellow
            Write-Host "  处置三步指引：" -ForegroundColor Yellow
            Write-Host "    1) 递增 Directory.Build.props 中的版本号（推荐），或以 -Version 传入新版本；" -ForegroundColor Yellow
            Write-Host "    2) 如确需同版本覆盖：清缓存 dotnet nuget locals global-packages --clear（或逐包删除上述目录）；" -ForegroundColor Yellow
            Write-Host "    3) 确认必须同版本重打时，使用 -ForceSameVersion 逃生开关重跑本脚本。" -ForegroundColor Yellow
            exit 1
        }
        Write-Host "版本递增校验：目标版本 $normalizedTarget 已存在于全局缓存，-ForceSameVersion 已放行（后果自负）。" -ForegroundColor DarkYellow
        Write-Host ""
    } else {
        Write-Host "版本递增校验通过：全局缓存中不存在 $normalizedTarget 的同版本包。" -ForegroundColor DarkGray
        Write-Host ""
    }
}

if ($Configuration -ne "Release") {
    Write-Host "!!! 警告：以 $Configuration 配置打包。Debug 程序集未做优化，不得作为发布包！！！" -ForegroundColor Red
    Write-Host "!!!        请确保输出目录与发布目录分离（-OutputDir）。                                ！！！" -ForegroundColor Red
    Write-Host ""
}

# 清理旧的包
$ArtifactsDir = Join-Path $RootDir $OutputDir
if (Test-Path $ArtifactsDir) {
    Write-Host "清理旧的包文件..." -ForegroundColor Yellow
    Remove-Item $ArtifactsDir -Recurse -Force
}
New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null
Write-Host ""

# 还原依赖
Write-Host "还原依赖..." -ForegroundColor Yellow
dotnet restore $Solution
if ($LASTEXITCODE -ne 0) {
    Write-Host "还原依赖失败！" -ForegroundColor Red
    exit 1
}
Write-Host "还原完成" -ForegroundColor Green
Write-Host ""

# -------------------------------------------------------------------------
# 打包前显式构建一次，pack 全程 --no-build：
#   若每个 dotnet pack 各自触发增量构建，排在被引用方之后的引用工程打包时可能改写
#   bin\<Configuration> 中已被打进包的同名 DLL，使「包内 DLL ↔ bin 产物」比对失配，
#   而是否重建由增量 up-to-date 判定决定，属于非确定性误报。
#   构建一次定型后，打包期间 bin 不再变化，校验基准取本次构建产物快照，
#   「包内容 = 本次 <Configuration> 构建输出」这一命题与打包循环的执行顺序解耦。
# -------------------------------------------------------------------------
Write-Host "构建 ($Configuration)..." -ForegroundColor Yellow
dotnet build $Solution -c $Configuration
if ($LASTEXITCODE -ne 0) {
    Write-Host "构建失败！" -ForegroundColor Red
    exit 1
}
Write-Host "构建完成" -ForegroundColor Green
Write-Host ""

# 逐项目打包
$FailedProjects = @()

foreach ($project in $Projects) {
    $projectDir = Join-Path $RootDir (Split-Path $project -Parent)
    $projectName = (Split-Path $project -Leaf) -replace '\.csproj$', ''
    $csproj = Join-Path $projectDir "$projectName.csproj"

    if (-not (Test-Path $csproj)) {
        Write-Host "跳过 (未找到项目文件): $project" -ForegroundColor DarkGray
        continue
    }

    Write-Host "打包: $project" -ForegroundColor Yellow

    # --no-build：打包期间不得再触发构建（否则会改写 bin 内已被打进包的产物，造成校验基准漂移）
    dotnet pack $csproj -c $Configuration -o $ArtifactsDir --no-restore --no-build

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  失败: $project" -ForegroundColor Red
        $FailedProjects += $project
    } else {
        Write-Host "  成功: $project" -ForegroundColor Green
    }
}

Write-Host ""

# -------------------------------------------------------------------------
# 打包后校验
#   ① 包集合必须与打包清单一致 —— 防清单漂移导致漏发包/多发包；
#   ② 包内每个 DLL 必须与本次构建产物快照字节一致 ——
#      防「以 Debug 产物冒充 Release 发布」与陈旧错版打包。
# -------------------------------------------------------------------------
$ValidationFailures = New-Object System.Collections.Generic.List[string]
$nupkgs = @(Get-ChildItem $ArtifactsDir -Filter "*.nupkg" -File -ErrorAction SilentlyContinue)

# 包 ID 可含点、版本以数字开头：用非贪婪 ID + 数字开头的版本来切分文件名
# （prerelease 段自身可含点，如 1.0.2-beta.1）
$pkgNameRegex = '^(?<id>.+?)\.(?<version>\d[\w\.\-\+]*)\.nupkg$'
$producedPackages = @()
foreach ($p in $nupkgs) {
    if ($p.Name -match $pkgNameRegex) {
        if ($producedPackages -notcontains $Matches['id']) {
            $producedPackages += $Matches['id']
        }
    } else {
        $ValidationFailures.Add("无法从文件名解析包 ID/版本: $($p.Name)")
        Write-Host "  [FAIL] 无法从文件名解析包 ID/版本: $($p.Name)" -ForegroundColor Red
    }
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  打包后校验（配置=$Configuration；基准=打包前构建产物快照，与打包循环时序解耦）" -ForegroundColor Cyan

foreach ($expected in $ExpectedPackages) {
    if ($producedPackages -notcontains $expected) {
        $ValidationFailures.Add("缺少预期包: $expected")
        Write-Host "  [FAIL] 缺少预期包: $expected" -ForegroundColor Red
    }
}
foreach ($produced in $producedPackages) {
    if ($ExpectedPackages -notcontains $produced) {
        $ValidationFailures.Add("产出未预期包: $produced")
        Write-Host "  [FAIL] 产出未预期包: $produced" -ForegroundColor Red
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

# 校验基准 = 本次构建完成后的一次性快照（而非打包循环内现场读取 bin）：
#   输出根有两类 —— Abstractions/Agent 经 BaseOutputPath=..\bin 汇总到仓库根
#   bin\<Configuration>；Generator 无 BaseOutputPath，落在自身 bin\<Configuration>。
#   同一程序集可能被多个 TFM 的输出目录同时包含（如被引用工程 CopyLocal），
#   故按文件名聚合候选列表，只要有任一候选与包内条目字节一致即通过。
$snapshotRoots = New-Object System.Collections.ArrayList
$rootBin = Join-Path $RootDir "bin\$Configuration"
if (Test-Path $rootBin) { [void]$snapshotRoots.Add($rootBin) }
foreach ($project in $Projects) {
    $projectBin = Join-Path (Join-Path $RootDir (Split-Path $project -Parent)) "bin\$Configuration"
    if ((Test-Path $projectBin) -and ($snapshotRoots -notcontains $projectBin)) {
        [void]$snapshotRoots.Add($projectBin)
    }
}

$binSnapshot = @{}          # key: 文件名（小写） → 候选列表（Directory=TFM 目录名 / Hash / LastWrite）
$snapshotFileCount = 0
foreach ($snapshotRoot in $snapshotRoots) {
    $snapshotFiles = @(Get-ChildItem -Path $snapshotRoot -Recurse -Filter "*.dll" -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(obj|ref)\\' })
    foreach ($snapshotFile in $snapshotFiles) {
        $key = $snapshotFile.Name.ToLowerInvariant()
        if (-not $binSnapshot.ContainsKey($key)) {
            $binSnapshot[$key] = New-Object System.Collections.ArrayList
        }
        [void]$binSnapshot[$key].Add([pscustomobject]@{
                Directory = $snapshotFile.Directory.Name
                Hash      = (Get-FileHash -LiteralPath $snapshotFile.FullName -Algorithm SHA256).Hash
                LastWrite = $snapshotFile.LastWriteTime
            })
        $snapshotFileCount++
    }
}
if ($snapshotFileCount -eq 0) {
    Write-Host "  [WARN] 未找到 bin\$Configuration 产物（快照为空，后续比对将全部报缺）" -ForegroundColor Yellow
} else {
    Write-Host "  校验基准快照：$snapshotFileCount 个 DLL（$($snapshotRoots.Count) 个输出根）" -ForegroundColor DarkGray
}

$checkedDllCount = 0

foreach ($pkg in $nupkgs) {
    if ($pkg.Name -notmatch $pkgNameRegex) { continue }
    $zip = [System.IO.Compression.ZipFile]::OpenRead($pkg.FullName)
    try {
        $dllEntries = @($zip.Entries | Where-Object {
                $_.FullName -match '^(lib|analyzers|runtimes)/.*\.dll$'
            })
        foreach ($entry in $dllEntries) {
            $fileName = [System.IO.Path]::GetFileName($entry.FullName)
            # 包内 TFM 段（lib/net8.0/x.dll → net8.0；analyzers 无 TFM 段）
            $tfmSegment = if ($entry.FullName -match '^lib/([^/]+)/') { $Matches[1] } else { $null }

            $candidates = @()
            $snapshotKey = $fileName.ToLowerInvariant()
            if ($binSnapshot.ContainsKey($snapshotKey)) {
                $candidates = @($binSnapshot[$snapshotKey])
            }
            if ($tfmSegment) {
                $tfmMatched = @($candidates | Where-Object { $_.Directory -eq $tfmSegment })
                if ($tfmMatched.Count -gt 0) { $candidates = $tfmMatched }
            }

            if ($candidates.Count -eq 0) {
                $ValidationFailures.Add("$($pkg.Name) 中的 $fileName 在 bin\$Configuration 下不存在")
                Write-Host "  [FAIL] $($pkg.Name) → $fileName 不在 bin\$Configuration 产物中" -ForegroundColor Red
                continue
            }

            $stream = $entry.Open()
            $sha = [System.Security.Cryptography.SHA256]::Create()
            $entryHash = ([BitConverter]::ToString($sha.ComputeHash($stream)) -replace '-', '')
            $sha.Dispose()
            $stream.Dispose()

            $matched = @($candidates | Where-Object { $_.Hash -eq $entryHash })
            $candidateNames = ($candidates | ForEach-Object { $_.Directory } | Select-Object -Unique) -join '/'
            $checkedDllCount++
            if ($matched.Count -eq 0) {
                # 诊断信息：列出候选的 TFM/哈希前缀/写入时刻，便于区分
                # 「错版・陈旧打包」（哈希全不同且时刻早于本次构建）与「基准被外部构建污染」（时刻晚于本次构建）
                $candidateDetail = ($candidates |
                    ForEach-Object { "$($_.Directory):$($_.Hash.Substring(0, 8))@$($_.LastWrite.ToString('HH:mm:ss'))" } |
                    Select-Object -Unique) -join ', '
                $ValidationFailures.Add("$($pkg.Name) 的 $fileName 与 bin\$Configuration 快照产物均不一致（包内 $($entryHash.Substring(0, 8))；候选 $candidateDetail）：疑似错版/陈旧打包或基准被外部构建污染")
                Write-Host "  [FAIL] $($pkg.Name) → $fileName 不一致（包内 $($entryHash.Substring(0, 8)) vs 快照 $candidateDetail）" -ForegroundColor Red
            }
        }
    }
    finally {
        $zip.Dispose()
    }
}

if ($ValidationFailures.Count -eq 0) {
    Write-Host "  [ OK ] 包集合 $($producedPackages.Count) 个，DLL 与 bin\$Configuration 构建快照逐字节一致（校验 $checkedDllCount 个）" -ForegroundColor Green
}
Write-Host ""

Write-Host "========================================" -ForegroundColor Cyan

if ($FailedProjects.Count -gt 0 -or $ValidationFailures.Count -gt 0) {
    if ($FailedProjects.Count -gt 0) {
        Write-Host "  打包完成，以下项目失败:" -ForegroundColor Red
        foreach ($p in $FailedProjects) {
            Write-Host "    - $p" -ForegroundColor Red
        }
    }
    if ($ValidationFailures.Count -gt 0) {
        Write-Host "  打包后校验未通过，共 $($ValidationFailures.Count) 项:" -ForegroundColor Red
        foreach ($v in $ValidationFailures) {
            Write-Host "    - $v" -ForegroundColor Red
        }
    }
    exit 1
} else {
    Write-Host "  打包完成！共生成 $($nupkgs.Count) 个包（已通过配置与集合校验）" -ForegroundColor Green
    Write-Host "  输出目录: $ArtifactsDir" -ForegroundColor Cyan
}

Write-Host "========================================" -ForegroundColor Cyan
