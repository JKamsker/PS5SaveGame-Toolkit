<#
.SYNOPSIS
  Builds the app (all plugins are referenced via glob) and runs every test project.
.DESCRIPTION
  Each test project runs with its own TEMP/TMP folder, which is deleted afterwards, so helpers that
  write under Path.GetTempPath() cannot accumulate extracted samples between runs. The scratch root
  defaults to the system temp folder; set SAVETOOLKIT_TEST_TEMP to move it to another drive.
.PARAMETER Filter
  Optional substring; only test projects whose path contains it are run (e.g. -Filter Pragmata).
#>
param(
    [string]$Filter = '',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$ResultsDirectory = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$scratchRoot = if ($env:SAVETOOLKIT_TEST_TEMP) { $env:SAVETOOLKIT_TEST_TEMP } else { Join-Path ([IO.Path]::GetTempPath()) 'SaveToolkit.Tests' }
$savedTemp, $savedTmp = $env:TEMP, $env:TMP
$buildTemp = Join-Path $scratchRoot ("build-" + [guid]::NewGuid().ToString('N'))
Push-Location $root
try {
    New-Item -ItemType Directory -Force -Path $buildTemp | Out-Null
    $env:TEMP = $buildTemp; $env:TMP = $buildTemp
    dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj --configuration $Configuration --warnaserror
    $buildExit = $LASTEXITCODE
    $env:TEMP, $env:TMP = $savedTemp, $savedTmp
    if ($buildExit -ne 0) { throw 'Build failed.' }

    $projects = Get-ChildItem -Recurse -Filter '*.Tests.csproj' -Path tests, plugins |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.FullName -like "*$Filter*" } |
        Sort-Object FullName
    if (!$projects) { throw "No test projects matched '$Filter'." }
    if ($ResultsDirectory) {
        $ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
        New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
    }
    $failed = @()
    foreach ($p in $projects) {
        $runTemp = Join-Path $scratchRoot ("run-" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Force -Path $runTemp | Out-Null
        $env:TEMP = $runTemp; $env:TMP = $runTemp
        try {
            $testArgs = @('test', '--project', $p.FullName, '--configuration', $Configuration)
            if ($ResultsDirectory) {
                $testArgs += @('--results-directory', (Join-Path $ResultsDirectory $p.BaseName),
                    '--report-xunit-trx', '--report-xunit-trx-filename', 'results.trx')
            }
            dotnet @testArgs
            if ($LASTEXITCODE -ne 0) { $failed += $p.Name }
        }
        finally {
            $env:TEMP, $env:TMP = $savedTemp, $savedTmp
            Remove-Item -LiteralPath $runTemp -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
    if ($failed.Count -gt 0) { throw "Failing test projects: $($failed -join ', ')" }
}
finally {
    $env:TEMP, $env:TMP = $savedTemp, $savedTmp
    Remove-Item -LiteralPath $buildTemp -Recurse -Force -ErrorAction SilentlyContinue
    Pop-Location
}
