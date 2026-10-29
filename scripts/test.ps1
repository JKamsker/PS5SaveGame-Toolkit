<#
.SYNOPSIS
  Builds the app (all plugins are referenced via glob) and runs every test project.
.DESCRIPTION
  Each test project runs with its own TEMP/TMP folder, which is deleted afterwards, so helpers that
  write under Path.GetTempPath() cannot accumulate extracted samples between runs. The scratch root
  defaults to the system temp folder; set SAVETOOLKIT_TEST_TEMP to move it to another drive.
.PARAMETER Filter
  Optional substring; only test projects whose path contains it are run (e.g. -Filter Pragmata).
.PARAMETER Coverage
  Collect Cobertura coverage per project under ResultsDirectory (defaults to TestResults).
#>
param(
    [string]$Filter = '',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$ResultsDirectory = '',
    [switch]$Coverage
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$scratchPath = if ($env:SAVETOOLKIT_TEST_TEMP) { $env:SAVETOOLKIT_TEST_TEMP } else { Join-Path ([IO.Path]::GetTempPath()) 'SaveToolkit.Tests' }
$scratchRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($scratchPath)
$savedTemp, $savedTmp, $savedTmpDir = $env:TEMP, $env:TMP, $env:TMPDIR

function Remove-TestScratch([string]$Path) {
    if (!(Test-Path -LiteralPath $Path)) { return }
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    if ([IO.Path]::GetDirectoryName($resolved) -ne [IO.Path]::TrimEndingDirectorySeparator($scratchRoot)) {
        throw "Refusing to delete unexpected test directory: $resolved"
    }
    try { Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction Stop }
    catch { Write-Warning "Could not remove test scratch '${resolved}': $_" }
}

$buildTemp = Join-Path $scratchRoot ("build-" + [guid]::NewGuid().ToString('N'))
Push-Location -LiteralPath $root
try {
    New-Item -ItemType Directory -Force -Path $buildTemp | Out-Null
    $env:TEMP = $buildTemp; $env:TMP = $buildTemp; $env:TMPDIR = $buildTemp
    dotnet build src/SaveToolkit.Api/SaveToolkit.Api.csproj --configuration $Configuration --warnaserror
    $buildExit = $LASTEXITCODE
    $env:TEMP, $env:TMP, $env:TMPDIR = $savedTemp, $savedTmp, $savedTmpDir
    if ($buildExit -ne 0) { throw 'Build failed.' }

    $projects = Get-ChildItem -Recurse -Filter '*.Tests.csproj' -Path tests, plugins |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.FullName -like "*$Filter*" } |
        Sort-Object FullName
    if (!$projects) { throw "No test projects matched '$Filter'." }
    if ($Coverage -and !$ResultsDirectory) { $ResultsDirectory = Join-Path $root 'TestResults' }
    if ($ResultsDirectory) {
        $ResultsDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ResultsDirectory)
        New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
    }
    $failed = @()
    foreach ($p in $projects) {
        $runTemp = Join-Path $scratchRoot ("run-" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Force -Path $runTemp | Out-Null
        $env:TEMP = $runTemp; $env:TMP = $runTemp; $env:TMPDIR = $runTemp
        try {
            $testArgs = @('test', '--project', $p.FullName, '--configuration', $Configuration)
            if ($ResultsDirectory) {
                $testArgs += @('--results-directory', (Join-Path $ResultsDirectory $p.BaseName),
                    '--report-xunit-trx', '--report-xunit-trx-filename', 'results.trx')
            }
            if ($Coverage) {
                $testArgs += @('--coverage', '--coverage-output-format', 'cobertura',
                    '--coverage-output', (Join-Path $ResultsDirectory "$($p.BaseName)/coverage.cobertura.xml"))
            }
            dotnet @testArgs
            if ($LASTEXITCODE -ne 0) { $failed += $p.Name }
        }
        finally {
            $env:TEMP, $env:TMP, $env:TMPDIR = $savedTemp, $savedTmp, $savedTmpDir
            Remove-TestScratch $runTemp
        }
    }
    if ($failed.Count -gt 0) { throw "Failing test projects: $($failed -join ', ')" }
}
finally {
    $env:TEMP, $env:TMP, $env:TMPDIR = $savedTemp, $savedTmp, $savedTmpDir
    Remove-TestScratch $buildTemp
    Pop-Location
}
