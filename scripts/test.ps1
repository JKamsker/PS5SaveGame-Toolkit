<#
.SYNOPSIS
  Builds the app (all plugins are referenced via glob) and runs every test project.
.PARAMETER Filter
  Optional substring; only test projects whose path contains it are run (e.g. -Filter Pragmata).
#>
param([string]$Filter = '')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

    $projects = Get-ChildItem -Recurse -Filter '*.Tests.csproj' -Path tests, plugins |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.FullName -like "*$Filter*" }
    $failed = @()
    foreach ($p in $projects) {
        dotnet test --project $p.FullName
        if ($LASTEXITCODE -ne 0) { $failed += $p.Name }
    }
    if ($failed.Count -gt 0) { throw "Failing test projects: $($failed -join ', ')" }
}
finally {
    Pop-Location
}
