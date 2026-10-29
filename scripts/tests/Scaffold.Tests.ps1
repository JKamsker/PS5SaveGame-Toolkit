Invoke-Case 'scaffold escapes C# literals and refuses to overwrite' {
    $fixture = New-Fixture
    $script = Join-Path $fixture 'scripts/new-plugin.ps1'
    $game = "Quoted `"Game`" \ path`r`nnext @NAME@"
    & $script -Name ExampleGame -Id example-game -TitleIds 'PPSA00000' -GameName $game
    $source = Get-Content -LiteralPath (Join-Path $fixture 'plugins/ExampleGame/SaveToolkit.Plugins.ExampleGame/ExampleGamePlugin.cs') -Raw
    Assert-True ($source.Contains('"Quoted \"Game\" \\ path\r\nnext @NAME@"')) 'Game name must be escaped and token-like input must stay literal.'
    $files = @(Get-ChildItem -LiteralPath (Join-Path $fixture 'plugins') -Recurse -File)
    $before = @($files | Get-FileHash | ForEach-Object Hash) -join ','
    $rejected = $false
    try { & $script -Name ExampleGame -Id replacement } catch { $rejected = $true }
    Assert-True $rejected 'Existing plugins must be rejected.'
    $after = @($files | Get-FileHash | ForEach-Object Hash) -join ','
    Assert-True ($before -eq $after) 'Rejected scaffolding must not modify files.'
}

Invoke-Case 'scaffold includes current README sections, feature rows and five exclusion columns' {
    $fixture = New-Fixture
    & (Join-Path $fixture 'scripts/new-plugin.ps1') -Name ExampleGame -Id example-game -TitleIds PPSA00000, PPSA00001
    $readme = Get-Content -LiteralPath (Join-Path $fixture 'plugins/ExampleGame/README.md') -Raw
    foreach ($heading in @('Enabled writes and their evidence', 'Framework feature adoption',
            'Sources and attribution', 'SDK requests', 'Licence exclusions')) {
        Assert-True ($readme -match "(?m)^## $heading\r?$") "Missing README section: $heading"
    }
    foreach ($feature in 1..13) {
        Assert-True ($readme -match "(?m)^\| F$feature \|") "Missing feature row F$feature."
    }
    $exclusions = ($readme -split '## Licence exclusions', 2)[1]
    $table = @($exclusions -split '\r?\n' | Where-Object { $_.StartsWith('|') })
    Assert-True ($table.Count -eq 2) 'No-source example needs an empty table, not an invented refusal.'
    Assert-True ((($table[0] -split '\|').Count - 2) -eq 5) 'Licence exclusions need five columns.'
    Assert-True ($table[0] -eq '| Source URL / revision | Exact licence found | Refused material / reason | Alternative used | What would unblock reuse |') 'Exclusion columns must describe every rule 9 requirement.'
    Assert-True ($readme.Contains('Synthetic scaffold only')) 'The example must not claim real game support.'
    $conformance = Get-Content -LiteralPath (Join-Path $fixture 'plugins/ExampleGame/SaveToolkit.Plugins.ExampleGame.Tests/ExampleGameConformanceTests.cs') -Raw
    Assert-True ($conformance.Contains('["PPSA00000", "PPSA00001"]')) 'Conformance must retain every supplied title.'
}

Invoke-Case 'scaffold enforces metadata syntax and casing before writing files' {
    $fixture = New-Fixture
    foreach ($invalid in @(@{ TitleIds = 'PPSA00000"' }, @{ TitleIds = 'ppsa00000' },
            @{ Id = 'Uppercase' }, @{ Name = 'lowercase' })) {
        $parameters = @{ Name = 'InvalidTitle'; Id = 'invalid-title'; TitleIds = @('PPSA00000') }
        foreach ($key in $invalid.Keys) { $parameters[$key] = $invalid[$key] }
        $rejected = $false
        try { & (Join-Path $fixture 'scripts/new-plugin.ps1') @parameters }
        catch { $rejected = $true }
        Assert-True $rejected 'Name, ID and title IDs must satisfy the documented casing and metadata contracts.'
        Assert-True (!(Test-Path -LiteralPath (Join-Path $fixture "plugins/$($parameters.Name)"))) 'Invalid input must not leave a partial plugin.'
    }
}

Invoke-Case 'scaffold builds warning-free and runs conformance with absent and synthetic samples' {
    # Keep executable paths below Windows' process-launch limit, even under a long scratch root.
    $fixture = Join-Path $testRoot 'sdk repo'
    New-Item -ItemType Directory -Path $fixture | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'scripts') -Destination $fixture -Recurse
    # Copy only the shared dependency sources and build configuration, never private samples or outputs.
    foreach ($configuration in @('global.json', 'Directory.Build.props', 'Directory.Packages.props')) {
        Copy-Item -LiteralPath (Join-Path $sourceRoot $configuration) -Destination $fixture
    }
    foreach ($dependency in @('SaveToolkit.Sdk', 'SaveToolkit.Core', 'SaveToolkit.Testing')) {
        $dependencyRoot = Join-Path $sourceRoot "src/$dependency"
        foreach ($file in Get-ChildItem -LiteralPath $dependencyRoot -Recurse -File |
                Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' }) {
            $destination = Join-Path $fixture ([IO.Path]::GetRelativePath($sourceRoot, $file.FullName))
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $destination
        }
    }
    $scratch = Join-Path $fixture 'temp space'
    $samples = Join-Path $fixture 'synthetic samples'
    New-Item -ItemType Directory -Path $scratch, $samples | Out-Null
    $env:TEMP = $scratch; $env:TMP = $scratch; $env:TMPDIR = $scratch
    $env:SAVETOOLKIT_TEST_TEMP = $scratch
    # Invoke-Case restores temp settings; this additional sample setting belongs to this test alone.
    $savedArtifacts = $env:SAVETOOLKIT_ARTIFACTS
    $env:SAVETOOLKIT_ARTIFACTS = $samples
    Push-Location -LiteralPath $fixture
    try {
        foreach ($name in @('Demo', 'Generic')) {
            $parameters = @{ Name = $name; Id = $name.ToLowerInvariant(); GameName = "Quoted `"Game`" \ path`r`nnext @NAME@" }
            if ($name -eq 'Demo') { $parameters.TitleIds = @('PPSA00000') }
            & (Join-Path $fixture 'scripts/new-plugin.ps1') @parameters
            $project = Join-Path $fixture "plugins/$name/SaveToolkit.Plugins.$name.Tests/SaveToolkit.Plugins.$name.Tests.csproj"
            # Resolve dotnet from PATH, even if another script case defines a mock function.
            $dotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
            & $dotnet build $project --configuration Release --warnaserror
            Assert-True ($LASTEXITCODE -eq 0) 'Generated projects must compile under warnings-as-errors.'
            foreach ($mode in @('absent', 'present')) {
                # Create the known synthetic archive only after checking the missing-sample behavior.
                $archivePath = Join-Path $samples 'PPSA00000_saves_dec.zip'
                if ($mode -eq 'present') {
                    $zip = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
                    try {
                        $entry = $zip.CreateEntry('PPSA00000/sdimg_EXAMPLE/example.bin').Open()
                        try {
                            $entry.Write([byte[]]([Text.Encoding]::ASCII.GetBytes('STKDEMO1') +
                                @(3, 0, 0, 0, 2, 0, 0, 0, 10, 0, 0, 0, 20, 0, 0, 0, 0xAA, 0xBB)))
                        }
                        finally { $entry.Dispose() }
                    }
                    finally { $zip.Dispose() }
                }
                $results = Join-Path $fixture "results/$name/$mode"
                & $dotnet test --project $project --configuration Release --no-build --results-directory $results `
                    --report-xunit-trx --report-xunit-trx-filename results.trx
                Assert-True ($LASTEXITCODE -eq 0) "Generated tests failed ($name, samples $mode)."
                [xml]$report = Get-Content -LiteralPath (Join-Path $results 'results.trx') -Raw
                $counters = $report.TestRun.ResultSummary.Counters
                Assert-True ([int]$counters.failed -eq 0 -and [int]$counters.total -eq 15) 'Every generated test must be discovered without failures.'
                $expectedSkips = if ($mode -eq 'absent') { 5 } else { 0 }
                Assert-True ([int]$counters.notExecuted -eq $expectedSkips) "Expected $expectedSkips skips ($name, samples $mode)."
                # MutationFuzz is inherited from the conformance base (69e2f47) and needs a sample seed.
                foreach ($method in @('Open_BuildsValidSerializableView', 'Probe_ClaimsSampleFiles', 'Serialize_WithoutEdits_IsByteIdentical', 'Sample_OpensRecognizedFile', 'MutationFuzz')) {
                    $result = @($report.TestRun.Results.UnitTestResult | Where-Object { $_.testName -like "*.$method" })
                    $expectedOutcome = if ($mode -eq 'absent') { 'NotExecuted' } else { 'Passed' }
                    Assert-True ($result.Count -eq 1 -and $result[0].outcome -eq $expectedOutcome) "Unexpected $method result ($mode)."
                }
            }
            # Only the known synthetic archive is removed; the fixture itself is cleaned by self-test.ps1.
            Remove-Item -LiteralPath $archivePath
        }
    }
    finally {
        Pop-Location
        $env:SAVETOOLKIT_ARTIFACTS = $savedArtifacts
    }
}
