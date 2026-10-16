param([Parameter(Mandatory = $true)][string]$File)
$root = Split-Path -Parent $PSScriptRoot
$path = Join-Path $root $File
$lines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i].TrimStart().StartsWith('//')) { "{0}: {1}" -f ($i + 1), $lines[$i].Trim() }
}
