<#
.SYNOPSIS
  Enforces this plugin's C# size policy in builds and pre-commit checks.
#>
param()
$ErrorActionPreference = 'Stop'
$pluginRoot = Split-Path -Parent $PSScriptRoot
$exemptions = Get-Content -LiteralPath (Join-Path $pluginRoot 'code-size-exemptions.json') -Raw | ConvertFrom-Json -AsHashtable
$failures = @()

$files = Get-ChildItem -LiteralPath $pluginRoot -Recurse -File -Filter '*.cs' | Where-Object {
    $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Name -notlike '*.g.cs' -and $_.Name -notlike '*.generated.cs'
}
foreach ($file in $files) {
    $relative = [IO.Path]::GetRelativePath($pluginRoot, $file.FullName).Replace('\', '/')
    $lines = [IO.File]::ReadAllLines($file.FullName).Length
    if ($lines -gt 300) {
        Write-Warning "${relative}: $lines lines; prefer composition and files below 300 lines."
    }
    if ($lines -gt 500) {
        $reason = $exemptions[$relative]
        if ($reason -is [string] -and $reason.Trim().Length -ge 20) {
            Write-Warning "${relative}: hard-limit exception: $reason"
        } else {
            $failures += "${relative}: $lines lines exceeds the 500-line hard limit. Refactor or record a reviewed justification in code-size-exemptions.json."
        }
    }
}
if ($failures.Count -gt 0) { throw ($failures -join [Environment]::NewLine) }
