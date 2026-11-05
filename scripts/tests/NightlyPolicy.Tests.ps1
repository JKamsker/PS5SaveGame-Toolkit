Invoke-Case 'scheduled diagnostic jobs surface execution failures without gating scores' {
    foreach ($name in @('nightly-stryker', 'dependency-drift')) {
        $workflow = Get-Content -LiteralPath (Join-Path $sourceRoot ".github/workflows/$name.yml") -Raw
        Assert-True ($workflow -notmatch 'continue-on-error: true') "Execution errors must fail $name, so scheduled failures are visible."
    }
    $config = Get-Content -LiteralPath (Join-Path $sourceRoot 'tests/stryker-config.json') -Raw | ConvertFrom-Json
    Assert-True ($config.'stryker-config'.thresholds.break -eq 0) 'Mutation scores remain informational.'
    $stryker = Get-Content -LiteralPath (Join-Path $sourceRoot '.github/workflows/nightly-stryker.yml') -Raw
    Assert-True ($stryker.Contains('if: always()')) 'Failure diagnostics must still be uploaded.'
    Assert-True ($stryker.Contains('/stryker/output/**')) 'Retain Stryker diagnostic logs when no report is produced.'
    Assert-True ($stryker.Contains('--log-to-file')) 'Enable diagnostic logs even when test discovery fails.'
    Assert-True ($stryker.Contains('stryker_slices.py')) 'Use the tested stable partition helper.'
    Assert-True ($stryker.Contains('REQUESTED_SLICE: ${{ inputs.slice }}')) 'Pass dispatch input as data.'
    Assert-True ($stryker -notmatch '--mutate') 'Do not override the generated slice selection.'
    Assert-True ($stryker.Contains('steps.slice.outputs.count')) 'Artifact names identify slice/count.'
}

foreach ($reportState in @('survivors', 'missing', 'malformed')) {
    Invoke-Case "Stryker summary preserves informational scores but rejects unavailable reports ($reportState)" {
        $fixture = New-Fixture
        $workflow = Get-Content -LiteralPath (Join-Path $sourceRoot '.github/workflows/nightly-stryker.yml') -Raw
        $step = [regex]::Match($workflow,
            '(?m)^      - name: Summarize project mutation score\r?\n        if: always\(\)\r?\n        run: \|\r?\n(?<script>(?:          .*\r?\n|\r?\n)+)')
        Assert-True $step.Success 'Execute the actual summary step.'
        $scriptText = [regex]::Replace($step.Groups['script'].Value, '(?m)^          ', '')
        $scriptPath = Join-Path $fixture 'summary.ps1'
        Set-Content -LiteralPath $scriptPath -Value ("`$ErrorActionPreference = 'Stop'`n" + $scriptText)
        $reports = Join-Path $fixture 'output/reports'
        New-Item -ItemType Directory -Force $reports | Out-Null
        @{ project = 'Core'; slice = '3'; count = 9; files = @('Sessions/Workspace.cs') } |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $fixture 'slice.json')
        if ($reportState -eq 'survivors') {
            @{ files = @{ 'source.cs' = @{ mutants = @(@{ status = 'Survived' }, @{ status = 'NoCoverage' }) } } } |
                ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $reports 'mutation-report.json')
        }
        elseif ($reportState -eq 'malformed') {
            Set-Content -LiteralPath (Join-Path $reports 'mutation-report.json') -Value '{'
        }
        $savedScratch, $savedSummary, $savedProject = $env:MUTATION_SCRATCH, $env:GITHUB_STEP_SUMMARY, $env:MUTATION_PROJECT
        try {
            $env:MUTATION_SCRATCH = $fixture
            $env:GITHUB_STEP_SUMMARY = Join-Path $fixture 'summary.md'
            $env:MUTATION_PROJECT = 'Core'
            & (Join-Path $PSHOME 'pwsh') -NoProfile -File $scriptPath *> (Join-Path $fixture 'summary.log')
            $code = $LASTEXITCODE
            if ($reportState -eq 'survivors') {
                Assert-True ($code -eq 0) 'Surviving/uncovered mutants and a zero score must not fail the job.'
                $summary = Get-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Raw
                Assert-True ($summary.Contains('**0.00%**')) 'The real summary must report the zero score.'
                Assert-True ($summary.Contains('**3/9**')) 'Summary identifies the slice.'
                Assert-True ($summary.Contains('Sessions/Workspace.cs')) 'Summary lists selected files.'
            }
            else { Assert-True ($code -ne 0) 'A missing or malformed report must fail the job.' }
        }
        finally {
            $env:MUTATION_SCRATCH, $env:GITHUB_STEP_SUMMARY, $env:MUTATION_PROJECT = $savedScratch, $savedSummary, $savedProject
        }
    }
}
