<#
.SYNOPSIS
    Runs bounded Core/SDK mutation sweeps and retains compact results outside the checkout.
#>
param(
    [ValidateSet('Editing', 'Export', 'Storage', 'Sessions', 'Collections', 'ActionPlans')]
    [string[]]$Area = @('Editing', 'Export', 'Storage', 'Sessions', 'Collections', 'ActionPlans'),
    [ValidateSet('before', 'after')][string]$Label = 'after',
    [string]$ScratchDirectory = 'D:/Temp/saveeditor/test-scratch/rev-mutation-core'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$scratchRoot = [IO.Path]::GetFullPath($ScratchDirectory)
New-Item -ItemType Directory -Force -Path $scratchRoot | Out-Null
$reportPath = Join-Path $scratchRoot 'stryker-current'
$configPath = Join-Path $scratchRoot 'stryker-current-config.json'
$mtpConfigPath = Join-Path $scratchRoot 'mutation.testconfig.json'
$frameworkExclusions = @('*Allocation*', '*Api*', '*.SdkHelpers.*', '*MutationFuzz*', '*Conformance*',
    '*SaveMutation*', '*TestScratch*', '*Documentation*')
$filters = @{
    Editing = @('*')
    Export = @('*Export*', '*DocumentGroup*', '*SessionOutputOwnership*')
    Storage = @('*Import*', '*DiskSaveSet*', '*OverlaySaveSet*', '*Export*', '*DocumentGroup*')
    Sessions = @('*')
    Collections = @('*.SdkHelpers.InMemoryCollection*')
    ActionPlans = @('*.SdkHelpers.ActionPlans*')
}
$patterns = @{
    Editing = '**/Editing/*.cs'; Export = '**/Export/*.cs'; Storage = '**/Storage/*.cs'
    Sessions = '**/Sessions/**/*.cs'; Collections = '**/Collections/*.cs'; ActionPlans = '**/ActionPlans.cs'
}
$variables = @('SAVETOOLKIT_TEST_TEMP', 'TEMP', 'TMP', 'TMPDIR', 'SAVETOOLKIT_MUTATION_CONFIG')
$saved = @{}
foreach ($variable in $variables) { $saved[$variable] = [Environment]::GetEnvironmentVariable($variable) }

function Remove-CurrentReport {
    if (!(Test-Path -LiteralPath $reportPath)) { return }
    $resolved = (Resolve-Path -LiteralPath $reportPath).Path
    if ([IO.Path]::GetDirectoryName($resolved) -ne [IO.Path]::TrimEndingDirectorySeparator($scratchRoot)) {
        throw "Refusing to delete unexpected report directory: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

Push-Location (Join-Path $PSScriptRoot 'SaveToolkit.Core.Tests')
try {
    $env:SAVETOOLKIT_TEST_TEMP = Join-Path $scratchRoot 'tests'
    $env:TEMP = $env:TMP = $env:TMPDIR = $scratchRoot
    $env:SAVETOOLKIT_MUTATION_CONFIG = $mtpConfigPath
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'Local tool restore failed.' }

    foreach ($name in $Area) {
        Remove-CurrentReport
        $exclusions = @(if ($name -in @('Editing', 'Sessions')) { $frameworkExclusions } else { '*Allocation*' })
        if ($Label -eq 'before') { $exclusions += '*MutationEffectiveness*' }
        $options = @{ 'filter-not-class' = $exclusions }
        if ($name -notin @('Editing', 'Sessions')) { $options['filter-class'] = $filters[$name] }
        @{ commandLineOptions = $options } |
            ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $mtpConfigPath
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'stryker-config.json') -Destination $configPath -Force
        $project = if ($name -in @('Collections', 'ActionPlans')) { 'Sdk' } else { 'Core' }
        $logPath = Join-Path $scratchRoot "$($name.ToLowerInvariant())-$Label.log"
        Write-Host "Mutating $name ($Label); log: $logPath"
        dotnet stryker --config-file $configPath --project "SaveToolkit.$project.csproj" `
            --mutate $patterns[$name] --output $reportPath --skip-version-check *> $logPath
        if ($LASTEXITCODE -ne 0) { throw "Stryker failed for $name; see $logPath" }

        $report = Get-Content (Join-Path $reportPath 'reports/mutation-report.json') -Raw | ConvertFrom-Json -AsHashtable
        $fragment = if ($name -eq 'ActionPlans') { '/ActionPlans.cs' } else { "/$name/" }
        $mutants = @(
            foreach ($file in $report.files.GetEnumerator()) {
                if (!(('/' + $file.Key.Replace('\', '/')).Contains($fragment))) { continue }
                foreach ($mutant in $file.Value.mutants) {
                    if ($mutant.status -eq 'Ignored') { continue }
                    $mutant['file'] = $file.Key
                    $mutant.Remove('coveredBy')
                    $mutant.Remove('killedBy')
                    $mutant
                }
            }
        )
        $counts = @{}
        foreach ($mutant in $mutants) {
            if (!$counts.ContainsKey($mutant.status)) { $counts[$mutant.status] = 0 }
            $counts[$mutant.status]++
        }
        $detected = @($mutants | Where-Object { $_.status -in @('Killed', 'Timeout') }).Count
        $valid = @($mutants | Where-Object { $_.status -in @('Killed', 'Timeout', 'Survived', 'NoCoverage') }).Count
        $score = if ($valid) { 100.0 * $detected / $valid } else { $null }
        $summary = @{ area = $name; label = $Label; counts = $counts; detected = $detected
            valid = $valid; score = $score; mutants = $mutants }
        $summaryPath = Join-Path $scratchRoot "$($name.ToLowerInvariant())-$Label-summary.json"
        $summary | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $summaryPath
        Write-Host "$name score: $score ($detected/$valid); summary: $summaryPath"
        Remove-CurrentReport
    }
}
finally {
    Pop-Location
    foreach ($variable in $variables) { [Environment]::SetEnvironmentVariable($variable, $saved[$variable]) }
}
