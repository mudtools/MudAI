# Bulk exact-once text replacement driven by a JSON map. ASCII-only script (CJK lives in the JSON).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/bulk-replace.ps1 -Map .docs/replacements-p1.json
param([Parameter(Mandatory = $true)][string]$Map)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$mapPath = Join-Path $root $Map
if (-not (Test-Path $mapPath)) { throw "map not found: $mapPath" }

$entries = Get-Content $mapPath -Raw -Encoding UTF8 | ConvertFrom-Json
$ok = 0; $fail = 0
$byFile = @{}

foreach ($e in $entries) {
    if (-not $byFile.ContainsKey($e.file)) { $byFile[$e.file] = New-Object System.Collections.Generic.List[object] }
    $byFile[$e.file].Add($e)
}

foreach ($rel in $byFile.Keys) {
    $path = Join-Path $root ($rel -replace '/', '\')
    if (-not (Test-Path $path)) { $fail++; Write-Host ("MISSING FILE  {0}" -f $rel) -ForegroundColor Red; continue }
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    $text = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    foreach ($e in $byFile[$rel]) {
        $old = $e.old; $new = $e.new
        $count = ([regex]::Matches($text, [regex]::Escape($old))).Count
        if ($count -eq 0 -and $old.Contains("`n")) {
            $old = $old.Replace("`n", "`r`n")
            if ($new.Contains("`n") -and -not $new.Contains("`r`n")) { $new = $new.Replace("`n", "`r`n") }
            $count = ([regex]::Matches($text, [regex]::Escape($old))).Count
        }
        if ($count -eq 1) {
            $text = $text.Replace($old, $new)
            $ok++
        }
        elseif ($count -eq 0) {
            $fail++; Write-Host ("NOT FOUND ({0}x)  {1}  << {2}" -f $count, $rel, $e.old.Substring(0, [Math]::Min(60, $e.old.Length))) -ForegroundColor Yellow
        }
        else {
            $fail++; Write-Host ("AMBIGUOUS ({0}x)  {1}  << {2}" -f $count, $rel, $e.old.Substring(0, [Math]::Min(60, $e.old.Length))) -ForegroundColor Yellow
        }
    }
    [System.IO.File]::WriteAllText($path, $text, $utf8)
}
Write-Host ("applied={0} failed={1}" -f $ok, $fail)
if ($fail -gt 0) { exit 1 }
