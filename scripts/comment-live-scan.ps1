# Live comment metrics for the three runtime projects (mirrors CommentConventionScanner.Measure).
# ASCII-only on purpose (same reason as scripts/comment-audit.ps1): a UTF-8-without-BOM script with
# CJK literals is a parse error in Windows PowerShell 5.1. CJK regex words are built from code points.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/comment-live-scan.ps1 [-VerboseScan]
param([switch]$VerboseScan)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$projects = @('Mud.AI.Abstractions', 'Mud.AI.Agent', 'Mud.AI.Generator')

$anchorRe = '(?<![\w-])(?:DP-[A-Z]+\d+-\d+|R\d+-\d+[a-z]?|P\dD-\d+[a-z]?|P\d+-\d+[a-z]?|R-\d+|B\d+\.\d+|AT-[A-Z]\d+|AI-FD-[A-Z]+\d+|T\d+ / D\d+)(?![\w-])'

function Word([int[]]$codePoints) { return -join ($codePoints | ForEach-Object { [char]$_ }) }
$cIQian   = Word @(0x6B64, 0x524D)                    # CI-qian  ("before this")
$yuanXian = Word @(0x539F, 0x5148)                    # yuan-xian
$cengJing = Word @(0x66FE, 0x7ECF)                    # ceng-jing
$jiuImpl  = Word @(0x65E7, 0x5B9E, 0x73B0)            # jiu-shi-xian
$fangAn   = Word @(0x65B9, 0x6848, 0x521D, 0x7A3F)    # fang-an-chu-gao
$benLun   = Word @(0x672C, 0x8F6E, 0x5BA1, 0x67E5)    # ben-lun-shen-cha
$shangYi  = Word @(0x4E0A, 0x4E00, 0x7248)            # shang-yi-ban
$yiShan   = Word @(0x5DF2, 0x5220, 0x9664)            # yi-shan-chu
$yiYi     = Word @(0x5DF2, 0x79FB, 0x9664)            # yi-yi-chu
$huo      = Word @(0x6216)                            # huo
$dun      = Word @(0x3001)                            # dun-hao
$fSlash   = [char]0xFF0F                              # full-width solidus
$slash    = [char]0x2F                               # solidus
$narrRe = "$cIQian|$yuanXian|$cengJing|$jiuImpl|$fangAn|$benLun|$shangYi|(?<![$slash$fSlash])($yiShan|$yiYi)(?![$huo$dun$slash$fSlash])"

$anchorOcc = 0; $narrOcc = 0; $totalComments = 0; $overCap = 0

foreach ($p in $projects) {
    Get-ChildItem -Path (Join-Path $root $p) -Recurse -Filter *.cs -File | Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } | ForEach-Object {
        $rel = $_.FullName.Substring($root.Length + 1)
        $lines = Get-Content $_.FullName
        $comments = 0; $maxBlock = 0; $run = 0
        foreach ($s in $lines) {
            $t = $s.TrimStart()
            if (-not $t.StartsWith('//')) { $run = 0; continue }
            $comments++
            if ($t.StartsWith('/// <param') -or $t.StartsWith('/// <typeparam')) { $run = 0; continue }
            $run++; if ($run -gt $maxBlock) { $maxBlock = $run }
        }
        $totalComments += $comments
        $flags = @()
        if ($maxBlock -gt 20) { $flags += ("BLOCK>{0}" -f $maxBlock) }
        if ($comments -gt 120) { $flags += ("COMMENTS>{0}" -f $comments) }
        if ($flags) {
            $overCap++
            "{0,-75} {1,4}c maxBlock={2,-3} {3}" -f $rel, $comments, $maxBlock, ($flags -join ' ')
            if ($VerboseScan) {
                $run2 = 0; $start = 0; $bestStart = 0; $bestLen = 0
                for ($i = 0; $i -lt $lines.Count; $i++) {
                    $t = $lines[$i].TrimStart()
                    if (-not $t.StartsWith('//')) { $run2 = 0; continue }
                    if ($t.StartsWith('/// <param') -or $t.StartsWith('/// <typeparam')) { $run2 = 0; continue }
                    $run2++
                    if ($run2 -gt $bestLen) { $bestLen = $run2; $bestStart = $i - $run2 + 1 }
                }
                "    longest block: lines {0}-{1}" -f ($bestStart + 1), ($bestStart + $bestLen)
            }
        }
        if ($VerboseScan) {
            $m = Select-String -Path $_.FullName -Pattern $anchorRe -Encoding utf8
            if ($m) { $anchorOcc += $m.Count; foreach ($x in $m) { "ANCHOR|{0}|{1}|{2}" -f $rel, $x.LineNumber, $x.Line.Trim() } }
            $n = Select-String -Path $_.FullName -Pattern $narrRe -Encoding utf8
            if ($n) { $narrOcc += $n.Count; foreach ($x in $n) { "NARR|{0}|{1}|{2}" -f $rel, $x.LineNumber, $x.Line.Trim() } }
        }
    }
}
Write-Host ''
Write-Host ("TOTAL comments={0}  overCap-files={1}  anchor-occurrences={2}  narration-occurrences={3}" -f $totalComments, $overCap, $anchorOcc, $narrOcc)
