# -----------------------------------------------------------------------
#  作者：Mud Studio  版权所有 (c) Mud Studio 2026
#  Mud.AI 项目的版权、商标、专利和其他相关权利均受相应法律法规的保护。使用本项目应遵守相关法律法规和许可证的要求。
#  本项目主要遵循 MIT 许可证进行分发和使用。许可证位于源代码树根目录中的 LICENSE-MIT 文件。
#  不得利用本项目从事危害国家安全、扰乱社会秩序、侵犯他人合法权益等法律法规禁止的活动！任何基于本项目开发而产生的一切法律纠纷和责任，我们不承担任何责任！
# -----------------------------------------------------------------------

<#
.SYNOPSIS
    Mud.AI 构建质量门禁（CI-1）。

.DESCRIPTION
    依次执行：
      步骤 1  全 TFM Release 构建（--no-incremental）-> 断言 0 错误 / 0 NU1603 / 0 CS1750
      步骤 2  诊断白名单断言（HTTPCLIENT / MUD / FORM / AOT 零容忍）
      步骤 3  AotStrictMode 冒烟（net8.0，逐源工程）
      步骤 4  单元测试（逐 (工程, TFM) TRX，断言 total>0 / failed=0）
      步骤 5  dotnet format --verify-no-changes（默认仅告警，见 -StrictFormat）
      步骤 6  打包内容门禁（opt-in -WithPackCheck；V-1/R5：引擎包 analyzer 资产路径）

    本脚本与飞书仓 `scripts/verify-build.ps1` 对应（迁移方案 §5.3 阶段 3 红线表）：
    标准构建 --no-incremental / AOT 严格逐源工程 + 0 错误 + AOT00x/IL2026/IL3050=0 /
    每 (工程, TFM) 独立 TRX 且 total>0 / 0 CS1750 / 0 NU1603 / 0 HTTPCLIENT0xx /
    0 MUD001-002 / 0 FORM0xx / 注释审计（经步骤 4 的 CommentConventionContractGuards）/
    -Full 档包含 Timing 用例 + 严格格式校验。

    已从飞书仓门禁中**刻意剔除**的飞书域步骤（在新仓无对应物）：
      飞书仓步骤 0  Mud.HttpUtils.Generator 本地源↔全局缓存一致性（CACHE-1）。
                    新仓 nuget.org 单一源、自源全量构建，无跨仓本地源消费 ⇒ 不适用。
      飞书仓步骤 2  MUDFT/SDKT 源生成诊断。由 Mud.Feishu.AI.Tools 的引擎发射，属飞书域。
      飞书仓步骤 5  Redis 集成测试。新仓无集成测试工程。
      飞书仓步骤 2  FeishuToolSchemas.golden.txt 存在性。金快照属产品侧。

.PARAMETER StrictFormat
    将步骤 5 的格式差异视为门禁失败。默认仅告警，因为仓库存在历史格式偏差，
    强制通过会产生与本次改动无关的大量 diff。

.PARAMETER WithPackCheck
    启用步骤 6 打包内容门禁：`dotnet pack Mud.AI.slnx` 必须产出三个 .nupkg，
    且 Mud.AI.Generator.nupkg 内含 analyzers/dotnet/cs/Mud.AI.Generator.dll（V-1）。

.PARAMETER Fast
    日常开发快速档（测试体系治理 §三.2）：
    增量构建 + 单元测试（排除 Timing/ Stress 用例），跳过诊断计数断言（步骤 2）、
    AOT 严格冒烟（步骤 3）与格式校验（步骤 5）。
    ⚠️ 诊断断言在增量构建下必然失真：源生成器被整体跳过 ⇒ 日志里一条 AOT/HTTPCLIENT
    都没有，所有计数断言在"0 个诊断"上假绿——故 Fast 档**必须**跳过它们而不是照跑，
    诊断回归由 Standard/Full 档（或 CI）拦截。

.PARAMETER Full
    发版/夜间全量档：在默认档之上包含 Category=Timing 计时敏感用例，
    并把格式校验差异升级为门禁失败（等价 -StrictFormat）。

.EXAMPLE
    ./scripts/verify-build.ps1
    ./scripts/verify-build.ps1 -StrictFormat
    ./scripts/verify-build.ps1 -WithPackCheck
    ./scripts/verify-build.ps1 -Fast        # 日常开发快速档
    ./scripts/verify-build.ps1 -Full        # 发版/夜间全量档
#>
[CmdletBinding()]
param(
    [switch]$StrictFormat,
    [switch]$WithPackCheck,
    [switch]$Fast,
    [switch]$Full
)

# 档位互斥：-Fast 与 -Full 语义冲突（一个最小化一个最大化）。
if ($Fast -and $Full) {
    Write-Host '-Fast 与 -Full 不能同时使用。' -ForegroundColor Red
    exit 1
}
# -Full 隐含 StrictFormat（发版档的格式要求）
if ($Full) { $StrictFormat = $true }

$ErrorActionPreference = 'Continue'
$repoRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $repoRoot 'Mud.AI.slnx'

$failures = New-Object System.Collections.Generic.List[string]

function Assert-Zero {
    param(
        [string]$Name,
        [int]$Count,
        [string]$Hint = ''
    )
    if ($Count -ne 0) {
        $script:failures.Add("${Name}: 期望 0，实际 $Count。$Hint")
        Write-Host "  [FAIL] ${Name} = $Count" -ForegroundColor Red
    }
    else {
        Write-Host "  [ OK ] ${Name} = 0" -ForegroundColor Green
    }
}

Set-Location $repoRoot

# ---------------------------------------------------------------- 步骤 1
Write-Host "[步骤 1] 全 TFM Release 构建" -ForegroundColor Cyan

$buildLog = Join-Path $env:TEMP "mudai-verify-build-$([guid]::NewGuid().ToString('N')).log"

# ⚠️ `--no-incremental` 不是可选项：步骤 2 要读的诊断由源生成器/AOT 分析器产出，
#    而增量构建会**整体跳过生成器** ⇒ 日志里一条诊断都没有 ⇒ 所有断言在"0 个诊断"上
#    通过 —— 一个**不可能失败**的测量（详见飞书仓同款历史）。CI 在全新 runner 上构建
#    （必然全量），因此基线必须按**全量**口径取值。
#
#    Fast 档走增量构建换取反馈速度，代价是步骤 2 的诊断计数全部失真（生成器被跳过 ⇒ 恒 0
#    ⇒ 假绿）——故 Fast 档同时跳过步骤 2/3 断言（见下方 $Fast 分支的显式告警）。
$buildArgs = @('build', $solution, '-c', 'Release', '--nologo')
if (-not $Fast) { $buildArgs += '--no-incremental' }
dotnet @buildArgs 2>&1 | Tee-Object -FilePath $buildLog | Out-Null

$cs1750 = (Select-String -Path $buildLog -Pattern 'error CS1750' -AllMatches).Count
$nu1603 = (Select-String -Path $buildLog -Pattern 'NU1603' -AllMatches).Count
$errors = (Select-String -Path $buildLog -Pattern ': error ' -AllMatches).Count

Assert-Zero -Name '编译错误'        -Count $errors -Hint "详见 $buildLog"
Assert-Zero -Name 'CS1750 断路'     -Count $cs1750 -Hint '生成器默认值回归或依赖缓存陈旧'
Assert-Zero -Name 'NU1603 版本漂移' -Count $nu1603 -Hint '声明版本与解析版本不一致'

# ---------------------------------------------------------------- 步骤 2
Write-Host "[步骤 2] 诊断白名单断言" -ForegroundColor Cyan
if ($Fast) {
    # Fast 档的构建是增量的：源生成器被整体跳过 ⇒ 日志里一条诊断都没有，
    # 以下所有计数断言都会在"0 个诊断"上假绿。故本步骤显式跳过并告警。
    Write-Host "  [SKIP] Fast 档：诊断计数断言在增量构建下必然假绿，已跳过（由 Standard/Full 档或 CI 拦截）" -ForegroundColor Yellow
}
else {
    Assert-Zero -Name 'HTTPCLIENT0xx' -Count ((Select-String -Path $buildLog -Pattern 'HTTPCLIENT0\d\d' -AllMatches).Count)
    Assert-Zero -Name 'MUD001/002'    -Count ((Select-String -Path $buildLog -Pattern 'MUD00[12]' -AllMatches).Count)
    Assert-Zero -Name 'FORM0xx'       -Count ((Select-String -Path $buildLog -Pattern 'FORM0\d\d' -AllMatches).Count)
    Assert-Zero -Name 'AOT001-007'    -Count ((Select-String -Path $buildLog -Pattern 'AOT00[1-7]' -AllMatches).Count) -Hint 'AOT006 已在 netstandard2.0/net6.0 豁免，net8+ 必须净零'
}

# ---------------------------------------------------------------- 步骤 3
Write-Host "[步骤 3] AotStrictMode 冒烟（net8.0，源项目）" -ForegroundColor Cyan
if ($Fast) {
    Write-Host "  [SKIP] Fast 档：AOT 严格冒烟跳过（由 Standard/Full 档或 CI 执行）" -ForegroundColor Yellow
}
else {
# 两处必须注意，否则本步骤会「假绿」：
#  ① 不能对整个解决方案使用 -f net8.0。单 TFM 源生成器工程（Mud.AI.Generator 为 netstandard2.0）
#     不含 net8.0 目标，MSBuild 会报 NETSDK1005「资产文件没有 net8.0 的目标」，构建实际失败；
#  ② 只断言 AOT00x 计数会掩盖上述失败（构建失败时 AOT00x 天然为 0），必须同时断言错误数=0。
# 因此改为逐个构建**源项目**（含 net8.0 目标者），并同时断言错误数与 IL2026/IL3050。
# glob 'Mud.*' 覆盖三个 Mud.AI.* 源工程（Leave-of-coverage 防护 R-6：
#   AI 三包的 AOT 严格覆盖随迁移转入本仓，不得静默蒸发）。
$sourceRoots = @(Get-ChildItem -Path $repoRoot -Directory -Filter 'Mud.*' -ErrorAction SilentlyContinue)
$strictProjects = @($sourceRoots |
    ForEach-Object { Get-ChildItem -Path $_.FullName -Filter '*.csproj' -File -ErrorAction SilentlyContinue } |
    Where-Object { $_.FullName -notmatch '\\(obj|bin)\\' } |
    Sort-Object FullName)
# 单 TFM 源项目（netstandard2.0 源生成器宿主等）不含 net8.0 目标，
# 用 -f net8.0 构建会报 NETSDK1005。此处按求值后的 TargetFrameworks 过滤（与步骤 4 同口径），
# 仅对真正含 net8.0 目标的源项目做严格冒烟。
$strictProjects = @(foreach ($proj in $strictProjects) {
    $tfmRaw = (dotnet msbuild $proj.FullName -getProperty:TargetFrameworks -p:Configuration=Release -nologo 2>$null |
        Where-Object { $_ -match '^net' } | Select-Object -Last 1)
    if (-not $tfmRaw) {
        $tfmRaw = (dotnet msbuild $proj.FullName -getProperty:TargetFramework -p:Configuration=Release -nologo 2>$null |
            Where-Object { $_ -match '^net' } | Select-Object -Last 1)
    }
    if (-not $tfmRaw) {
        Write-Host "  [SKIP] $($proj.Name)：无法解析 TFM，保守跳过（仅 netstandard2.0 单 TFM 项目会出现）" -ForegroundColor Yellow
        continue
    }
    $tfmList = @($tfmRaw.Split(';') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($tfmList -notcontains 'net8.0') {
        Write-Host "  [SKIP] $($proj.Name)：无 net8.0 目标（$($tfmList -join ';')），不适用 AOT 严格冒烟" -ForegroundColor Yellow
        continue
    }
    $proj
})
$strictLog = Join-Path $env:TEMP "mudai-verify-strict-$([guid]::NewGuid().ToString('N')).log"
if ($strictProjects.Count -eq 0) {
    $script:failures.Add('未找到任何源项目，AotStrictMode 冒烟无法执行')
    Write-Host '  [FAIL] 未找到任何源项目' -ForegroundColor Red
}
else {
    # ③ 必须强制重编译（--no-incremental）。CoreCompile 的"最新"判定只看输入/输出时间戳，
    #    **不比较 csc 命令行**；步骤 1 已用同一 TFM 构建过一次，本步骤即便带上
    #    -p:AotStrictMode=true 也会命中「正在跳过目标 CoreCompile」，于是 IL2026/IL3050
    #    一条都不产生，步骤 3 恒为 [ OK ]。
    foreach ($proj in $strictProjects) {
        Write-Host "  -> $($proj.Name)" -ForegroundColor DarkGray
        dotnet build $proj.FullName -c Release -f net8.0 -p:AotStrictMode=true --no-incremental --nologo 2>&1 |
            Tee-Object -FilePath $strictLog -Append | Out-Null
    }
    Assert-Zero -Name 'AotStrictMode 构建错误' -Count ((Select-String -Path $strictLog -Pattern ': error ' -AllMatches).Count) -Hint "详见 $strictLog"
    Assert-Zero -Name 'AotStrictMode AOT00x'    -Count ((Select-String -Path $strictLog -Pattern 'AOT00[1-7]' -AllMatches).Count) -Hint "详见 $strictLog"
    Assert-Zero -Name 'AotStrictMode IL2026'    -Count ((Select-String -Path $strictLog -Pattern 'IL2026' -AllMatches).Count) -Hint "详见 $strictLog"
    Assert-Zero -Name 'AotStrictMode IL3050'    -Count ((Select-String -Path $strictLog -Pattern 'IL3050' -AllMatches).Count) -Hint "详见 $strictLog"
}
}

# ---------------------------------------------------------------- 步骤 4
Write-Host "[步骤 4] 单元测试" -ForegroundColor Cyan

# 门禁按 TRX 解析（locale 无关），并对"每个测试工程都必须有结果"逐个断言（按 .slnx 中 Tests/** 清单）。
$trxDir = Join-Path $env:TEMP "mudai-verify-trx-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $trxDir -Force | Out-Null
$testLog = Join-Path $env:TEMP "mudai-verify-test-$([guid]::NewGuid().ToString('N')).log"

# 同时设置中文 locale（兼容旧日志解析）和 TRX logger（locale 无关）
$env:DOTNET_CLI_UI_LANGUAGE = 'zh-CN'

# 逐测试工程 + 逐 TFM 运行：单个 (工程, TFM) 组合单独指定结果目录与文件名，
# 避免同名 TRX 互相覆盖（同一工程多 TFM 会写同名 TRX）。
$testProjectPaths = @()
if (Test-Path $solution) {
    $slnxContent = Get-Content -LiteralPath $solution -Raw -Encoding utf8
    # 仅匹配 Project 条目（排除 File 条目，如 Tests/Directory.Build.props）
    foreach ($m in [regex]::Matches($slnxContent, '<Project Path="(Tests/[^"]+)"')) {
        $testProjectPaths += (Join-Path $repoRoot ($m.Groups[1].Value -replace '/', '\'))
    }
}
if ($testProjectPaths.Count -eq 0) {
    $script:failures.Add('未从 .slnx 解析到任何测试工程')
    Write-Host '  [FAIL] 未从 .slnx 解析到任何测试工程' -ForegroundColor Red
}

$testRuns = @()
foreach ($testProject in $testProjectPaths) {
    $projectName = [System.IO.Path]::GetFileNameWithoutExtension($testProject)
    # TFM 必须取**求值后**的属性：测试工程把 <TargetFrameworks> 放在 Tests/Directory.Build.props，
    # 只用文本正则扫自身 csproj 会得到空集，该工程会被静默跳过。
    $tfmRaw = (dotnet msbuild $testProject -getProperty:TargetFrameworks -p:Configuration=Release -nologo 2>$null |
        Where-Object { $_ -match '^net' } | Select-Object -Last 1)
    if (-not $tfmRaw) {
        $tfmRaw = (dotnet msbuild $testProject -getProperty:TargetFramework -p:Configuration=Release -nologo 2>$null |
            Where-Object { $_ -match '^net' } | Select-Object -Last 1)
    }
    $tfms = if ($tfmRaw) {
        @($tfmRaw.Split(';') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    }
    else { @($null) }

    foreach ($tfm in $tfms) {
        $label = if ($tfm) { "$projectName.$tfm" } else { $projectName }
        $testRuns += [pscustomobject]@{ Label = $label; Project = $testProject; Tfm = $tfm }
    }
}

# 目标 TFM 的运行时缺失时 testhost 会「启动后中止」——此时 VSTest 仍会落盘一个 total=0 的 TRX，
# 退出码为 1。这里显式按运行时可用性过滤并单独播报，避免"全都没跑却绿灯"。
$installedRuntimeMajors = @(dotnet --list-runtimes 2>$null |
    ForEach-Object { if ($_ -match '^Microsoft\.NETCore\.App (\d+)\.') { [int]$Matches[1] } } |
    Sort-Object -Unique)

$testExit = 0
$skippedRuns = New-Object System.Collections.Generic.List[string]
$executedLabels = New-Object System.Collections.Generic.List[string]
$runTotals = @{}
foreach ($run in $testRuns) {
    if ($run.Tfm -and $run.Tfm -match '^net(\d+)\.') {
        $major = [int]$Matches[1]
        if ($installedRuntimeMajors -notcontains $major) {
            $skippedRuns.Add("$($run.Label)（未安装 .NET $major 运行时）")
            Write-Host "  -- $($run.Label)：跳过（未安装 .NET $major 运行时）" -ForegroundColor Yellow
            continue
        }
    }

    $runDir = Join-Path $trxDir $run.Label
    New-Item -ItemType Directory -Path $runDir -Force | Out-Null

    Write-Host "  -> $($run.Label)" -ForegroundColor DarkGray
    $testArgs = @('test', $run.Project, '-c', 'Release', '--no-build', '--nologo',
        '--results-directory', $runDir, '--logger', "trx;LogFileName=$($run.Label).trx")
    if ($run.Tfm) { $testArgs += @('-f', $run.Tfm) }
    # xUnit 的 [Trait] **不会**自动排除用例——`dotnet test` 无 --filter 时执行全部。
    # 压力/长稳用例（Category=Stress）与计时敏感用例（Category=Timing）在门禁负载下会抖动，
    # Standard/Fast 档一律排除，仅 -Full 档包含。
    $testFilter = if ($Full) { 'Category!=Stress' } else { 'Category!=Stress&Category!=Timing' }
    $testArgs += @('--filter', $testFilter)
    dotnet @testArgs 2>&1 | Tee-Object -FilePath $testLog -Append | Out-Null
    if ($LASTEXITCODE -ne 0) { $testExit = $LASTEXITCODE }
    $executedLabels.Add($run.Label)
}
$testRunLabels = @($executedLabels)

if ($skippedRuns.Count -gt 0) {
    Write-Host "  [WARN] 因缺少运行时未执行的组合（本机不阻断；CI 需补齐对应运行时）: $($skippedRuns -join ', ')" -ForegroundColor Yellow
}

# --- TRX 解析（locale 无关）---
$failed = 0
$passed = 0
$total = 0
$trxAssemblies = @{}
$failedTestNames = New-Object System.Collections.Generic.List[string]

$trxFiles = Get-ChildItem -Path $trxDir -Filter '*.trx' -File -Recurse -ErrorAction SilentlyContinue
if ($trxFiles -and $trxFiles.Count -gt 0) {
    foreach ($trx in $trxFiles) {
        try {
            [xml]$doc = Get-Content -LiteralPath $trx.FullName -Raw -Encoding utf8
            # TRX 结构：<TestRun><ResultSummary><Counters .../></ResultSummary><Results>...
            $counters = $doc.TestRun.ResultSummary.Counters
            if ($counters) {
                $failed += [int]$counters.failed
                $passed += [int]$counters.passed
                $total += [int]$counters.total
                $trxAssemblies[$trx.BaseName] = $true
                $runTotals[$trx.BaseName] = [int]$counters.total
            }
            # 收集失败用例名
            $testResults = $doc.TestRun.Results.UnitTestResult
            if ($testResults) {
                foreach ($r in $testResults) {
                    if ($r.outcome -eq 'failed') {
                        $testName = $r.testName
                        if (-not $testName -and $r.TestMethod) {
                            $testName = "$($r.TestMethod.className).$($r.TestMethod.name)"
                        }
                        $failedTestNames.Add($testName)
                    }
                }
            }
        }
        catch {
            Write-Host "  [WARN] 解析 TRX 失败: $($trx.Name) - $_" -ForegroundColor Yellow
        }
    }
}

# --- 中文摘要正则兜底（当 TRX 解析无结果时回退）---
if ($trxAssemblies.Count -eq 0) {
    Write-Host "  [WARN] 未发现 TRX 文件，回退到日志正则解析" -ForegroundColor Yellow
    foreach ($m in (Select-String -Path $testLog -Pattern '失败:\s*(\d+)' -AllMatches).Matches) {
        $failed += [int]$m.Groups[1].Value
    }
    foreach ($m in (Select-String -Path $testLog -Pattern '通过:\s*(\d+)' -AllMatches).Matches) {
        $passed += [int]$m.Groups[1].Value
    }
    $ranAssemblies = (Select-String -Path $testLog -Pattern '(已通过!|失败!)\s*-\s*失败' -AllMatches).Count
    if ($ranAssemblies -eq 0 -and $failed -eq 0) {
        # 英文回退
        foreach ($m in (Select-String -Path $testLog -Pattern 'Failed!\s*--\s*Failed:\s*(\d+)' -AllMatches).Matches) {
            $failed += [int]$m.Groups[1].Value
        }
        $ranAssemblies = (Select-String -Path $testLog -Pattern '(Passed!|Failed!)\s*--\s*Failed' -AllMatches).Count
    }
}

# --- 断言：每个（测试工程, TFM）组合都必须有结果 ---
$missingProjects = @()
foreach ($expected in $testRunLabels) {
    $projectTrx = @(Get-ChildItem -Path (Join-Path $trxDir $expected) -Filter '*.trx' -File -ErrorAction SilentlyContinue)
    if ($projectTrx.Count -eq 0) {
        $missingProjects += $expected
    }
}

if ($missingProjects.Count -gt 0) {
    $script:failures.Add("以下测试工程未产出结果: $($missingProjects -join ', ')（详见 $testLog）")
    Write-Host "  [FAIL] 未产出结果的测试工程: $($missingProjects -join ', ')" -ForegroundColor Red
}

# --- 断言：每个已执行的（工程, TFM）组合都必须真的跑过用例 ---
# testhost 启动失败（缺运行时、架构不符等）时 VSTest 仍会写出一个 total=0 的 TRX 并以退出码 1 结束。
# 仅断言"TRX 存在"会把这种"什么都没跑"当成通过；这里追加 total>0 的门槛。
$emptyRuns = @($testRunLabels | Where-Object {
        (-not $runTotals.ContainsKey($_)) -or ($runTotals[$_] -le 0)
    })
if ($emptyRuns.Count -gt 0) {
    $script:failures.Add("以下测试运行组合未执行任何用例（testhost 启动失败？）: $($emptyRuns -join ', ')")
    Write-Host "  [FAIL] 未执行任何用例的组合: $($emptyRuns -join ', ')" -ForegroundColor Red
}

if ($failed -ne 0) {
    $script:failures.Add("单元测试失败 $failed 项（详见 $testLog）")
    Write-Host "  [FAIL] 单元测试失败 = $failed" -ForegroundColor Red
    if ($failedTestNames.Count -gt 0) {
        Write-Host "  失败用例:" -ForegroundColor Red
        foreach ($name in $failedTestNames) {
            Write-Host "    - $name" -ForegroundColor Red
        }
    }
}
else {
    Write-Host "  [ OK ] 单元测试失败 = 0（通过 $passed / 总计 $total）" -ForegroundColor Green
    if ($testExit -ne 0) {
        Write-Host "  [WARN] dotnet test 退出码为 $testExit，但已执行组合均产出有效 TRX 且未执行用例数为 0" -ForegroundColor Yellow
    }
}

# 清理 TRX 临时目录
if (Test-Path $trxDir) {
    Remove-Item $trxDir -Recurse -Force -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------- 步骤 5
if ($Fast) {
    Write-Host "[步骤 5] 代码格式校验（Fast 档跳过）" -ForegroundColor Cyan
}
else {
Write-Host "[步骤 5] 代码格式校验" -ForegroundColor Cyan
dotnet format $solution --verify-no-changes --no-restore 2>&1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    if ($StrictFormat) {
        $script:failures.Add("dotnet format --verify-no-changes 未通过")
        Write-Host "  [FAIL] 存在格式差异，请执行 dotnet format $solution" -ForegroundColor Red
    }
    else {
        Write-Host "  [WARN] 存在格式差异（默认不阻断；仓库含历史格式偏差）。" -ForegroundColor Yellow
        Write-Host "         如需强制通过：dotnet format $solution，或加 -StrictFormat 使本步成为门禁失败。" -ForegroundColor Yellow
    }
}
else {
    Write-Host "  [ OK ] 格式校验通过" -ForegroundColor Green
}
}

# ---------------------------------------------------------------- 步骤 6
if ($WithPackCheck) {
    Write-Host "[步骤 6] 打包内容门禁（V-1/R5：引擎包 analyzer 资产路径）" -ForegroundColor Cyan
    $nupkgDir = Join-Path $repoRoot 'nupkg'
    # 清理旧产物后重新打包
    if (Test-Path $nupkgDir) { Remove-Item $nupkgDir -Recurse -Force -ErrorAction SilentlyContinue }
    $packLog = Join-Path $env:TEMP "mudai-verify-pack-$([guid]::NewGuid().ToString('N')).log"
    dotnet pack $solution -c Release --output $nupkgDir --no-build 2>&1 | Tee-Object -FilePath $packLog | Out-Null
    if ($LASTEXITCODE -ne 0) {
        $script:failures.Add("dotnet pack 失败（详见 $packLog）")
        Write-Host "  [FAIL] dotnet pack 失败" -ForegroundColor Red
    }
    else {
        # 三个包都必须产出（包身份独立于飞书仓；缺失即打包面缺口）。
        $expectedPackages = @('Mud.AI.Abstractions', 'Mud.AI.Agent', 'Mud.AI.Generator')
        foreach ($pid in $expectedPackages) {
            $pkg = Get-ChildItem $nupkgDir -Filter "$pid.*.nupkg" -File -ErrorAction SilentlyContinue | Select-Object -First 1
            if (-not $pkg) {
                $script:failures.Add("打包缺失：$pid（期望 $pid.*.nupkg 于 $nupkgDir）")
                Write-Host "  [FAIL] 缺失包 $pid" -ForegroundColor Red
            }
            else {
                Write-Host "  [ OK ] $($pkg.Name)" -ForegroundColor Green
            }
        }

        # V-1/R5：引擎包必须把 dll 打进 analyzers/dotnet/cs/，否则 OutputItemType="Analyzer"
        # 的消费方（飞书仓）会"编译全绿但零生成产物"（FeishuToolSchemas 缺失）。
        $generatorPkg = Get-ChildItem $nupkgDir -Filter 'Mud.AI.Generator.*.nupkg' -File -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($generatorPkg) {
            Add-Type -AssemblyName System.IO.Compression.FileSystem
            $zip = [System.IO.Compression.ZipFile]::OpenRead($generatorPkg.FullName)
            try {
                $analyzerEntry = $zip.Entries | Where-Object { $_.FullName -eq 'analyzers/dotnet/cs/Mud.AI.Generator.dll' } | Select-Object -First 1
                if (-not $analyzerEntry) {
                    $script:failures.Add("Mud.AI.Generator 包内缺少 analyzers/dotnet/cs/Mud.AI.Generator.dll（analyzer 形态契约，R5）")
                    Write-Host "  [FAIL] 引擎包 analyzer 资产路径缺失" -ForegroundColor Red
                    Write-Host "         包内条目：" -ForegroundColor Yellow
                    $zip.Entries | ForEach-Object { Write-Host "           $($_.FullName)" -ForegroundColor DarkGray }
                }
                else {
                    Write-Host "  [ OK ] analyzers/dotnet/cs/Mud.AI.Generator.dll 存在" -ForegroundColor Green
                }
            }
            finally {
                $zip.Dispose()
            }
        }
    }
}
else {
    Write-Host '[步骤 6] 打包内容门禁（跳过；加 -WithPackCheck 启用）' -ForegroundColor DarkGray
}

# ---------------------------------------------------------------- 汇总
Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host "门禁未通过，共 $($failures.Count) 项：" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
    exit 1
}

Write-Host '门禁全部通过。' -ForegroundColor Green
exit 0
