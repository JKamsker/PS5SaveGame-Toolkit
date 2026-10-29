<#
.SYNOPSIS Runs opt-in synthetic BenchmarkDotNet workloads and retains a Markdown summary.
.DESCRIPTION
  Requires PowerShell 7 and the SDK from global.json. Quick uses ShortRun; Full uses the
  default BenchmarkDotNet job. Scratch defaults to SAVETOOLKIT_TEST_TEMP, then system temp.
  On the owner's workstation select D:/Temp/saveeditor/test-scratch/benchmarks explicitly.
#>
[CmdletBinding()]
param(
    [string]$ScratchDirectory = '',
    [ValidateSet('Quick', 'Full')][string]$Job = 'Quick',
    [string]$Filter = '*'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (!$ScratchDirectory) {
    $ScratchDirectory = if ($env:SAVETOOLKIT_TEST_TEMP) { $env:SAVETOOLKIT_TEST_TEMP }
        else { Join-Path ([IO.Path]::GetTempPath()) 'SaveToolkit.Benchmarks' }
}
$scratchRoot = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ScratchDirectory)
$runRoot = Join-Path $scratchRoot ('bench-' + [guid]::NewGuid().ToString('N'))
$artifacts = Join-Path $runRoot 'artifacts'
$tempRoot = Join-Path $runRoot 'temp'
$workRoot = Join-Path $runRoot 'work'
$variables = @('TEMP', 'TMP', 'TMPDIR', 'SAVETOOLKIT_BENCHMARK_SCRATCH')
$original = @{}
foreach ($variable in $variables) { $original[$variable] = [Environment]::GetEnvironmentVariable($variable) }

New-Item -ItemType Directory -Force -Path $artifacts, $tempRoot, $workRoot | Out-Null
Push-Location -LiteralPath $root
try {
    $env:TEMP = $env:TMP = $env:TMPDIR = $tempRoot
    $env:SAVETOOLKIT_BENCHMARK_SCRATCH = $workRoot
    $project = Join-Path $root 'tests/benchmarks/SaveToolkit.Benchmarks/SaveToolkit.Benchmarks.csproj'
    $arguments = @('run', '--project', $project, '--configuration', 'Release', '--',
        '--filter', $Filter, '--artifacts', $artifacts)
    if ($Job -eq 'Quick') { $arguments += @('--job', 'short') }
    dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Benchmarks failed (exit $LASTEXITCODE). Inspect $artifacts" }
    $reports = @(Get-ChildItem -LiteralPath (Join-Path $artifacts 'results') -Filter '*-report-github.md' -ErrorAction SilentlyContinue)
    if (!$reports) { throw "No benchmark reports were produced. Inspect $artifacts" }
    $summary = Join-Path $runRoot 'summary.md'
    $lines = @('# Synthetic performance baseline', '',
        "Captured (UTC): $([DateTime]::UtcNow.ToString('O'))", '',
        "Job: $Job; filter: $Filter. Indicative local measurements; see docs/PERFORMANCE.md.", '')
    foreach ($report in $reports | Sort-Object Name) {
        $lines += Get-Content -LiteralPath $report.FullName
        $lines += ''
    }
    Set-Content -LiteralPath $summary -Value $lines -Encoding utf8
    Write-Host "Benchmark summary: $summary"
}
finally {
    foreach ($variable in $variables) {
        if ($null -eq $original[$variable]) { Remove-Item -LiteralPath "Env:$variable" -ErrorAction SilentlyContinue }
        else { [Environment]::SetEnvironmentVariable($variable, $original[$variable]) }
    }
    Pop-Location
    # Delete only this invocation's data/temp; reports and logs remain available for review.
    foreach ($path in @($workRoot, $tempRoot)) {
        $resolved = [IO.Path]::GetFullPath($path)
        if ([IO.Path]::GetDirectoryName($resolved) -ne $runRoot) {
            throw "Refusing to delete unexpected benchmark directory: $resolved"
        }
        try { Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction Stop }
        catch { Write-Warning "Could not remove benchmark scratch '${resolved}': $_" }
    }
}
