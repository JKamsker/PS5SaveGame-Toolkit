param(
    [string]$OutputDirectory = 'D:/Temp/saveeditor/reviews/perf-finalfantasy16/comparisons',
    [string]$BaselineCommit = '6e9f1d6285788c8b71749a36b31edf53a93c53a3',
    [string]$ArtifactsDirectory = 'C:/Users/Jonas/repos/private/JKamsker/PS5SaveGame-Toolkit/artifacts',
    [ValidateRange(1, 10)][int]$Runs = 3
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if ([IO.Path]::GetPathRoot($outputPath) -ne 'D:\') { throw 'Benchmark scratch must be on D:.' }
$runDirectory = Join-Path $outputPath ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Performance.csproj'), (Join-Path $PSScriptRoot 'MeasurePerformance.cs') -Destination $runDirectory

$variables = @('SAVETOOLKIT_ARTIFACTS', 'SAVETOOLKIT_TEST_TEMP', 'TEMP', 'TMP', 'DOTNET_TieredCompilation')
$previous = @{}
foreach ($name in $variables) { $previous[$name] = [Environment]::GetEnvironmentVariable($name) }
Push-Location -LiteralPath $repoRoot
try {
    $env:SAVETOOLKIT_ARTIFACTS = $ArtifactsDirectory
    $env:SAVETOOLKIT_TEST_TEMP = 'D:/Temp/saveeditor/test-scratch'
    $env:TEMP = 'D:/Temp/saveeditor/test-scratch/review-perf-finalfantasy16'
    $env:TMP = $env:TEMP
    New-Item -ItemType Directory -Path $env:TEMP -Force | Out-Null
    # Avoid tier-promotion timing differences between early and late benchmark cases.
    $env:DOTNET_TieredCompilation = '0'

    $archive = Join-Path $runDirectory 'baseline.zip'
    git archive --format=zip "--output=$archive" $BaselineCommit -- plugins/FinalFantasy16/SaveToolkit.Plugins.FinalFantasy16
    if ($LASTEXITCODE -ne 0) { throw 'Cannot archive baseline plugin.' }
    $baselineRoot = Join-Path $runDirectory 'baseline'
    Expand-Archive -LiteralPath $archive -DestinationPath $baselineRoot
    Copy-Item -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Destination $baselineRoot
    $baselineProject = Join-Path $baselineRoot 'plugins/FinalFantasy16/SaveToolkit.Plugins.FinalFantasy16/SaveToolkit.Plugins.FinalFantasy16.csproj'
    # Compile the original plugin against the SAME current SDK/Testing as the changed plugin.
    [xml]$project = Get-Content -LiteralPath $baselineProject -Raw
    $project.SelectSingleNode('//ProjectReference').SetAttribute('Include', (Join-Path $repoRoot 'src/SaveToolkit.Sdk/SaveToolkit.Sdk.csproj'))
    $project.Save($baselineProject)
    $harness = Join-Path $runDirectory 'Performance.csproj'
    foreach ($iteration in 1..$Runs) {
        foreach ($variant in @('before', 'after')) {
            $pluginProject = if ($variant -eq 'before') { $baselineProject } else {
                Join-Path $repoRoot 'plugins/FinalFantasy16/SaveToolkit.Plugins.FinalFantasy16/SaveToolkit.Plugins.FinalFantasy16.csproj'
            }
            dotnet run --project $harness --configuration Release "-p:RepoRoot=$repoRoot" "-p:PluginProject=$pluginProject" |
                Tee-Object -FilePath (Join-Path $runDirectory "$variant-$iteration.log")
            if ($LASTEXITCODE -ne 0) { throw "$variant benchmark failed." }
        }
        $beforeHashes = (Select-String -LiteralPath (Join-Path $runDirectory "before-$iteration.log") -Pattern 'behavior sha256=').Line
        $afterHashes = (Select-String -LiteralPath (Join-Path $runDirectory "after-$iteration.log") -Pattern 'behavior sha256=').Line
        if ($beforeHashes.Count -ne 3 -or ($beforeHashes -join "`n") -cne ($afterHashes -join "`n")) {
            throw 'Before/after view, query, plan or no-op output fingerprints differ.'
        }
    }
    Write-Output "Benchmark source, original plugin snapshot and logs: $runDirectory"
}
finally {
    Pop-Location
    foreach ($name in $variables) { [Environment]::SetEnvironmentVariable($name, $previous[$name]) }
}
