param([string]$PluginRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
$failed = $false
Get-ChildItem -LiteralPath $PluginRoot -Filter *.cs -Recurse |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    ForEach-Object {
        $count = @(Get-Content -LiteralPath $_.FullName).Count
        if ($count -gt 500) {
            Write-Error -ErrorAction Continue "$($_.Name): $count lines exceeds the hard limit of 500."
            $failed = $true
        } elseif ($count -gt 300) {
            Write-Warning "$($_.Name): $count lines exceeds the recommended limit of 300."
        }
    }
if ($failed) { exit 1 }
