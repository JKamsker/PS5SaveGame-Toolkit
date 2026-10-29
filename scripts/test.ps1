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
.PARAMETER MaxParallel
  Maximum concurrent test processes (default 2). Projects build together before tests start.
#>
param(
    [string]$Filter = '',
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug',
    [string]$ResultsDirectory = '',
    [switch]$Coverage,
    [ValidateRange(1, 32)][int]$MaxParallel = 2
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$scratchPath = if ($env:SAVETOOLKIT_TEST_TEMP) { $env:SAVETOOLKIT_TEST_TEMP } else { Join-Path ([IO.Path]::GetTempPath()) 'SaveToolkit.Tests' }
$scratchRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($scratchPath)
$savedTemp, $savedTmp, $savedTmpDir = $env:TEMP, $env:TMP, $env:TMPDIR
$savedPipeDir = $env:TESTINGPLATFORM_PIPE_DIRECTORY
# MTP's coverage/monitor IPC uses Unix domain sockets, limited to 103 bytes. The per-project
# scratch path is longer, so keep the sockets in the short system temp folder on Unix.
if (!$IsWindows -and !$savedPipeDir) { $env:TESTINGPLATFORM_PIPE_DIRECTORY = '/tmp' }

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
$suiteWatch = [Diagnostics.Stopwatch]::StartNew()
$runs = [Collections.Generic.List[object]]::new()
# Start-Job uses separate processes: thread jobs would race on process-wide environment variables.
# The same worker runs inline at degree 1, keeping the serial path cheap and easy to diagnose.
$runProject = {
    param($ProjectPath, $Configuration, $ResultsDirectory, $Coverage, $RunTemp, $Root)
    $ErrorActionPreference = 'Stop'
    $savedTemp, $savedTmp, $savedTmpDir = $env:TEMP, $env:TMP, $env:TMPDIR
    $savedScratch = $env:SAVETOOLKIT_TEST_TEMP
    $projectName = [IO.Path]::GetFileNameWithoutExtension($ProjectPath)
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $exitCode = -1
    try {
        Set-Location -LiteralPath $Root
        $env:TEMP = $RunTemp; $env:TMP = $RunTemp; $env:TMPDIR = $RunTemp
        $env:SAVETOOLKIT_TEST_TEMP = $RunTemp
        $testArgs = @('test', '--project', $ProjectPath, '--configuration', $Configuration, '--no-build')
        if ($ResultsDirectory) {
            $testArgs += @('--results-directory', (Join-Path $ResultsDirectory $projectName),
                '--report-xunit-trx', '--report-xunit-trx-filename', 'results.trx')
        }
        if ($Coverage) {
            $testArgs += @('--coverage', '--coverage-output-format', 'cobertura',
                '--coverage-output', (Join-Path $ResultsDirectory "$projectName/coverage.cobertura.xml"))
        }
        dotnet @testArgs *> (Join-Path $RunTemp 'output.log')
        $exitCode = $LASTEXITCODE
    }
    catch { Add-Content -LiteralPath (Join-Path $RunTemp 'output.log') -Value "$_" }
    finally {
        $env:TEMP, $env:TMP, $env:TMPDIR = $savedTemp, $savedTmp, $savedTmpDir
        $env:SAVETOOLKIT_TEST_TEMP = $savedScratch
    }
    [pscustomobject]@{ Project = $projectName; ExitCode = $exitCode; Seconds = $watch.Elapsed.TotalSeconds }
}
Push-Location -LiteralPath $root
try {
    $projects = @(Get-ChildItem -Recurse -Filter '*.Tests.csproj' -Path tests, plugins |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.FullName -like "*$Filter*" } |
        Sort-Object FullName)
    if (!$projects) { throw "No test projects matched '$Filter'." }
    New-Item -ItemType Directory -Force -Path $buildTemp | Out-Null
    $env:TEMP = $buildTemp; $env:TMP = $buildTemp; $env:TMPDIR = $buildTemp
    # One solution build shares the dependency graph, including the API and every plugin. This
    # also avoids concurrent test builds writing to the same dependency outputs.
    $buildProjects = @((Join-Path $root 'src/SaveToolkit.Api/SaveToolkit.Api.csproj')) + @($projects.FullName)
    $solution = @('<Solution>') + @($buildProjects | ForEach-Object {
        '  <Project Path="' + [Security.SecurityElement]::Escape($_) + '" />'
    }) + @('</Solution>')
    $solutionPath = Join-Path $buildTemp 'tests.slnx'
    Set-Content -LiteralPath $solutionPath -Value $solution
    $buildWatch = [Diagnostics.Stopwatch]::StartNew()
    dotnet build $solutionPath --configuration $Configuration --warnaserror
    $buildExit = $LASTEXITCODE
    $buildSeconds = $buildWatch.Elapsed.TotalSeconds
    $env:TEMP, $env:TMP, $env:TMPDIR = $savedTemp, $savedTmp, $savedTmpDir
    if ($buildExit -ne 0) { throw 'Build failed.' }

    if ($Coverage -and !$ResultsDirectory) { $ResultsDirectory = Join-Path $root 'TestResults' }
    if ($ResultsDirectory) {
        $ResultsDirectory = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ResultsDirectory)
        New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null
    }
    $failed = @()
    $timings = [Collections.Generic.List[object]]::new()
    for ($offset = 0; $offset -lt $projects.Count; $offset += $MaxParallel) {
        $batch = @()
        foreach ($p in $projects[$offset..([Math]::Min($offset + $MaxParallel, $projects.Count) - 1)]) {
            $runTemp = Join-Path $scratchRoot ("run-" + [guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Force -Path $runTemp | Out-Null
            Set-Content -LiteralPath (Join-Path $runTemp 'output.log') -Value ''
            $run = [pscustomobject]@{ Project = $p; Temp = $runTemp; Job = $null; Result = $null }
            $runs.Add($run)
            $batch += $run
            $arguments = @($p.FullName, $Configuration, $ResultsDirectory, [bool]$Coverage, $runTemp, $root)
            if ($MaxParallel -eq 1) { $run.Result = & $runProject @arguments }
            else { $run.Job = Start-Job -ScriptBlock $runProject -ArgumentList $arguments }
        }
        # Replay complete project logs in sorted order, regardless of completion order.
        foreach ($run in $batch) {
            if ($run.Job) {
                $run.Job | Wait-Job | Out-Null
                try { $run.Result = Receive-Job -Job $run.Job }
                catch { Add-Content -LiteralPath (Join-Path $run.Temp 'output.log') -Value "Worker failed: $_" }
                finally { Remove-Job -Job $run.Job; $run.Job = $null }
            }
            Get-Content -LiteralPath (Join-Path $run.Temp 'output.log')
            if ($null -eq $run.Result -or $run.Result.ExitCode -ne 0) { $failed += $run.Project.Name }
            if ($run.Result) {
                $timings.Add(($run.Result | Select-Object Project, ExitCode, Seconds))
                Write-Host ("Project {0}: {1:F3}s" -f $run.Project.BaseName, $run.Result.Seconds)
            }
            Remove-TestScratch $run.Temp
        }
    }
    if ($ResultsDirectory) {
        [pscustomobject]@{ BuildSeconds = $buildSeconds; TotalSeconds = $suiteWatch.Elapsed.TotalSeconds
            MaxParallel = $MaxParallel; Projects = @($timings.ToArray()) } |
            ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $ResultsDirectory 'project-timings.json')
    }
    if ($failed.Count -gt 0) { throw "Failing test projects: $($failed -join ', ')" }
}
finally {
    $env:TEMP, $env:TMP, $env:TMPDIR = $savedTemp, $savedTmp, $savedTmpDir
    $env:TESTINGPLATFORM_PIPE_DIRECTORY = $savedPipeDir
    foreach ($run in $runs) {
        if ($run.Job) { Stop-Job -Job $run.Job; Remove-Job -Job $run.Job }
        Remove-TestScratch $run.Temp
    }
    Remove-TestScratch $buildTemp
    Pop-Location
}
