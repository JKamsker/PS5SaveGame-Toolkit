param()

$ErrorActionPreference = 'Stop'
$pluginRoot = Split-Path -Parent $PSScriptRoot
$warningLimit = 300
$hardLimit = 399
$failed = $false

Get-ChildItem -LiteralPath $pluginRoot -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    ForEach-Object {
        $lines = @(Get-Content -LiteralPath $_.FullName).Count
        $relativePath = [IO.Path]::GetRelativePath($pluginRoot, $_.FullName)
        if ($lines -gt $hardLimit) {
            Write-Host "ERROR: $relativePath has $lines lines (maximum $hardLimit). Split by responsibility."
            $failed = $true
        }
        elseif ($lines -gt $warningLimit) {
            Write-Warning "$relativePath has $lines lines (recommended maximum $warningLimit)."
        }
    }

if ($failed) { exit 1 }
Write-Host 'BG3 source-size check passed.'
