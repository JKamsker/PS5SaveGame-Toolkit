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
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
