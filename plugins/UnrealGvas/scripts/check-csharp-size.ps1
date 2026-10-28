param([string]$Root = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$warningLimit = 300
$hardLimit = 500
$failed = $false
Get-ChildItem -LiteralPath $Root -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    ForEach-Object {
        $lineCount = @(Get-Content -LiteralPath $_.FullName).Count
        if ($lineCount -gt $hardLimit) {
            Write-Output "error: $($_.Name) has $lineCount lines; hard limit is $hardLimit. Refactor by composition."
            $failed = $true
        }
        elseif ($lineCount -gt $warningLimit) {
            Write-Warning "$($_.Name) has $lineCount lines; recommended limit is $warningLimit."
        }
    }
if ($failed) { exit 1 }
