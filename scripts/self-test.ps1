<# .SYNOPSIS Runs dependency-free script regressions in an isolated scratch directory. #>
[CmdletBinding()]
param([string]$ScratchDirectory = [IO.Path]::GetTempPath())

$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path -Parent $PSScriptRoot
$scratchRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ScratchDirectory)
$testRoot = Join-Path $scratchRoot ('script-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$passed = 0
$failed = 0

function Assert-True([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
}

function New-Fixture {
    $fixture = Join-Path $testRoot ([guid]::NewGuid().ToString('N') + ' repo space')
    New-Item -ItemType Directory -Path $fixture | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'scripts') -Destination $fixture -Recurse
    return $fixture
}

function Invoke-Case([string]$Name, [scriptblock]$Test) {
    $originalLocation = Get-Location
    $originalExitCode = $global:LASTEXITCODE
    $variables = @('TEMP', 'TMP', 'TMPDIR', 'SAVETOOLKIT_TEST_TEMP')
    $original = @{}
    foreach ($variable in $variables) { $original[$variable] = [Environment]::GetEnvironmentVariable($variable) }
    try {
        & $Test
        $script:passed++
        Write-Host "PASS $Name"
    }
    catch {
        $script:failed++
        Write-Host "FAIL ${Name}: $_" -ForegroundColor Red
    }
    finally {
        Set-Location -LiteralPath $originalLocation.Path
        $global:LASTEXITCODE = $originalExitCode
        foreach ($variable in $variables) { [Environment]::SetEnvironmentVariable($variable, $original[$variable]) }
    }
}

try {
    foreach ($failure in @('none', 'native', 'missing-report')) {
        Invoke-Case "benchmark runner isolates scratch, reports and restores environment ($failure)" {
            $fixture = New-Fixture
            $caller = Join-Path $fixture 'caller space'
            New-Item -ItemType Directory -Path $caller | Out-Null
            Set-Location -LiteralPath $caller
            $savedBenchmarkScratch = $env:SAVETOOLKIT_BENCHMARK_SCRATCH
            $savedTemp, $savedTmp, $savedTmpDir = $env:TEMP, $env:TMP, $env:TMPDIR
            $calls = [Collections.Generic.List[object]]::new()
            function dotnet {
                $artifactPath = $args[[array]::IndexOf($args, '--artifacts') + 1]
                $calls.Add(@{ Arguments = $args; Temp = $env:TEMP; Work = $env:SAVETOOLKIT_BENCHMARK_SCRATCH; Artifacts = $artifactPath })
                Assert-True ($env:TEMP -eq $env:TMP -and $env:TEMP -eq $env:TMPDIR) 'All temp variables must match.'
                Assert-True (Test-Path -LiteralPath $env:TEMP) 'Temp must exist before dotnet runs.'
                if ($failure -eq 'none') {
                    $results = Join-Path $artifactPath 'results'
                    New-Item -ItemType Directory -Path $results | Out-Null
                    Set-Content -LiteralPath (Join-Path $results 'Workflow-report-github.md') '| Method | Mean |'
                }
                $global:LASTEXITCODE = if ($failure -eq 'native') { 17 } else { 0 }
            }
            $errorText = ''
            try { & (Join-Path $fixture 'scripts/bench.ps1') -ScratchDirectory 'relative scratch' }
            catch { $errorText = "$_" }
            Assert-True ($calls.Count -eq 1) 'Benchmarks must run once.'
            Assert-True ($calls[0].Arguments -contains 'short') 'Default job must be quick.'
            Assert-True ($calls[0].Arguments -contains 'Release') 'Benchmarks must use Release.'
            $runRoot = Split-Path -Parent $calls[0].Artifacts
            Assert-True ((Split-Path -Parent $runRoot) -eq (Join-Path $caller 'relative scratch')) 'Resolve scratch from caller.'
            Assert-True (!(Test-Path -LiteralPath $calls[0].Temp) -and !(Test-Path -LiteralPath $calls[0].Work)) 'Invocation data must be cleaned.'
            Assert-True (Test-Path -LiteralPath $calls[0].Artifacts) 'Keep benchmark logs/reports.'
            Assert-True ($env:TEMP -eq $savedTemp -and $env:TMP -eq $savedTmp -and $env:TMPDIR -eq $savedTmpDir -and
                $env:SAVETOOLKIT_BENCHMARK_SCRATCH -eq $savedBenchmarkScratch) 'Restore all caller environment values.'
            Assert-True ((Get-Location).Path -eq $caller) 'Restore caller location.'
            if ($failure -eq 'none') {
                Assert-True (!$errorText) "Unexpected failure: $errorText"
                Assert-True ((Get-Content -LiteralPath (Join-Path $runRoot 'summary.md') -Raw).Contains('| Method | Mean |')) 'Summary must include results.'
            }
            else {
                Assert-True ([bool]$errorText) 'Native failures and missing reports must fail the runner.'
                Assert-True (!(Test-Path -LiteralPath (Join-Path $runRoot 'summary.md'))) 'Do not publish a success summary after failure.'
            }
        }
    }
    foreach ($scratchSource in @('configured', 'system')) {
        Invoke-Case "benchmark Full job respects default scratch and filter ($scratchSource)" {
            $fixture = New-Fixture
            Set-Location -LiteralPath $fixture
            $env:SAVETOOLKIT_TEST_TEMP = if ($scratchSource -eq 'configured') { 'configured scratch' } else { $null }
            $expectedRoot = if ($scratchSource -eq 'configured') { Join-Path $fixture 'configured scratch' }
                else { Join-Path ([IO.Path]::GetTempPath()) 'SaveToolkit.Benchmarks' }
            $calls = [Collections.Generic.List[object]]::new()
            function dotnet {
                $artifactPath = $args[[array]::IndexOf($args, '--artifacts') + 1]
                $calls.Add(@{ Arguments = $args; Artifacts = $artifactPath })
                $results = Join-Path $artifactPath 'results'
                New-Item -ItemType Directory -Path $results -Force | Out-Null
                Set-Content -LiteralPath (Join-Path $results 'Workflow-report-github.md') '| Method | Mean |'
                $global:LASTEXITCODE = 0
            }
            & (Join-Path $fixture 'scripts/bench.ps1') -Job Full -Filter '*PageTable*'
            Assert-True ($calls.Count -eq 1) 'Benchmarks must run once.'
            Assert-True ($calls[0].Arguments -notcontains '--job') 'Full must use the default BenchmarkDotNet job.'
            Assert-True ($calls[0].Arguments -contains '*PageTable*') 'Preserve the wildcard filter argument.'
            $runRoot = Split-Path -Parent $calls[0].Artifacts
            Assert-True ((Split-Path -Parent $runRoot) -eq $expectedRoot) 'Honor configured scratch or system-temp fallback.'
            Assert-True ((Get-Content -LiteralPath (Join-Path $runRoot 'summary.md') -Raw).Contains('Job: Full;')) 'Summary must identify the job.'
        }
    }
    foreach ($test in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.Tests.ps1' | Sort-Object Name) {
        . $test.FullName
    }
    Write-Host "Script tests: $passed passed, $failed failed, 0 skipped."
    if ($failed) { throw "$failed script regression(s) failed." }
}
finally {
    $resolved = (Resolve-Path -LiteralPath $testRoot).Path
    if ([IO.Path]::GetDirectoryName($resolved) -ne [IO.Path]::TrimEndingDirectorySeparator($scratchRoot)) {
        throw "Refusing to delete unexpected test directory: $resolved"
    }
    try { Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction Stop }
    catch {
        # Scaffold builds leave the shared compiler server holding analyzer DLLs under the scratch
        # TEMP on Windows; stop it and retry once, then only warn so cleanup never hides results.
        dotnet build-server shutdown *> $null
        try { Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction Stop }
        catch { Write-Warning "Could not remove script-test scratch '${resolved}': $_" }
    }
}
