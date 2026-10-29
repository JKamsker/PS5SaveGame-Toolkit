foreach ($failure in @('none', 'build', 'test')) {
    Invoke-Case "test runner isolates Unix/Windows temp and cleans up ($failure)" {
        $fixture = New-Fixture
        foreach ($name in @('First', 'Second')) {
            $project = Join-Path $fixture "tests/$name/$name.Tests.csproj"
            New-Item -ItemType Directory -Path (Split-Path -Parent $project) -Force | Out-Null
            Set-Content -LiteralPath $project '<Project />'
        }
        New-Item -ItemType Directory -Path (Join-Path $fixture 'plugins') | Out-Null
        $caller = Join-Path $fixture 'caller space'
        New-Item -ItemType Directory -Path $caller | Out-Null
        Set-Location -LiteralPath $caller
        $env:SAVETOOLKIT_TEST_TEMP = 'isolated scratch'
        $env:TMPDIR = 'original Unix temp'
        $savedTemp, $savedTmp = $env:TEMP, $env:TMP
        $savedEncoding = [Console]::OutputEncoding.CodePage
        $calls = [Collections.Generic.List[object]]::new()
        function dotnet {
            $calls.Add(@{ Arguments = $args; Temp = $env:TEMP; Tmp = $env:TMP; TmpDir = $env:TMPDIR
                DotNetTemp = [IO.Path]::GetTempPath(); Scratch = $env:SAVETOOLKIT_TEST_TEMP
                Solution = $(if ($args[0] -eq 'build') { Get-Content -LiteralPath $args[1] -Raw }) })
            $global:LASTEXITCODE = if (($failure -eq 'build' -and $args[0] -eq 'build') -or
                ($failure -eq 'test' -and $args -match 'First.Tests.csproj')) { 17 } else { 0 }
        }
        $errorText = ''
        try { & (Join-Path $fixture 'scripts/test.ps1') -MaxParallel 1 -ResultsDirectory (Join-Path $testRoot 'results space') }
        catch { $errorText = "$_" }
        if ($failure -eq 'none') { Assert-True (!$errorText) "Unexpected failure: $errorText" }
        else { Assert-True ([bool]$errorText) 'A native failure must fail the script.' }
        Assert-True ($calls.Count -eq $(if ($failure -eq 'build') { 1 } else { 3 })) 'Test failures must not prevent later projects running.'
        foreach ($call in $calls) {
            Assert-True ($call.Arguments -notcontains '--coverage') 'Coverage must stay opt-in.'
            Assert-True ($call.Temp -eq $call.Tmp -and $call.Temp -eq $call.TmpDir) 'TEMP, TMP and TMPDIR must match.'
            Assert-True ([IO.Path]::TrimEndingDirectorySeparator($call.DotNetTemp) -eq $call.Temp) '.NET must actually select the isolated temp directory.'
            Assert-True ([IO.Path]::IsPathFullyQualified($call.Temp)) 'Child scratch must be absolute.'
            Assert-True ((Split-Path -Parent $call.Temp) -eq (Join-Path $caller 'isolated scratch')) 'Relative scratch must resolve from the caller.'
            Assert-True (!(Test-Path -LiteralPath $call.Temp)) 'Child scratch must be removed after success/failure.'
            if ($call.Arguments[0] -eq 'test') {
                Assert-True ($call.Arguments -contains '--no-build') 'Tests must reuse the shared build, without building API dependencies again.'
                Assert-True ($call.Arguments -notcontains '--max-threads') 'Serial execution must preserve the existing xUnit scheduling.'
                Assert-True ($call.Scratch -eq $call.Temp) 'Sample helpers must share the project-specific scratch directory.'
            }
        }
        Assert-True ($calls[0].Solution.Contains('SaveToolkit.Api.csproj') -and
            $calls[0].Solution.Contains('First.Tests.csproj') -and $calls[0].Solution.Contains('Second.Tests.csproj')) 'One build graph must contain the API and every selected project.'
        Assert-True ($env:TEMP -eq $savedTemp -and $env:TMP -eq $savedTmp -and $env:TMPDIR -eq 'original Unix temp') 'All temp variables must be restored.'
        Assert-True ([Console]::OutputEncoding.CodePage -eq $savedEncoding) 'The caller console encoding must be restored.'
        Assert-True ((Get-Location).Path -eq $caller) 'Caller location must be restored.'
    }
}

Invoke-Case 'parallel runner bounds workers, orders logs, retains reports and continues after failures' {
    $fixture = New-Fixture
    foreach ($name in @('First', 'Second', 'Third', 'Fourth')) {
        $project = Join-Path $fixture "tests/$name/$name.Tests.csproj"
        New-Item -ItemType Directory -Path (Split-Path -Parent $project) -Force | Out-Null
        Set-Content -LiteralPath $project '<Project />'
    }
    New-Item -ItemType Directory -Path (Join-Path $fixture 'plugins') | Out-Null
    $fakeBin = Join-Path $fixture 'fake bin'
    New-Item -ItemType Directory -Path $fakeBin | Out-Null
    # A real executable shim exercises the process boundary; parent-scope functions do not cross it.
    $fakeScript = Join-Path $fakeBin 'fake-dotnet.ps1'
    @'
if ($args[0] -eq 'build') {
    Copy-Item -LiteralPath $args[1] -Destination (Join-Path (Split-Path -Parent $PSScriptRoot) 'build.slnx')
    exit 0
}
$project = [IO.Path]::GetFileNameWithoutExtension($args[[array]::IndexOf($args, '--project') + 1])
$results = $args[[array]::IndexOf($args, '--results-directory') + 1]
New-Item -ItemType Directory -Force -Path $results | Out-Null
$start = [DateTime]::UtcNow
# The first batch must overlap, rather than merely observe short total wall time.
$barrier = Split-Path -Parent $results
Set-Content -LiteralPath (Join-Path $barrier "$project.started") -Value 'started'
if ($project -in @('First.Tests', 'Fourth.Tests')) {
    $limit = [DateTime]::UtcNow.AddSeconds(20)
    while (@(Get-ChildItem -LiteralPath $barrier -Filter '*.started').Count -lt 2) {
        if ([DateTime]::UtcNow -gt $limit) { throw 'Workers did not overlap.' }
        Start-Sleep -Milliseconds 25
    }
}
Start-Sleep -Milliseconds 150
if ($args -notcontains '--no-build') { throw 'Repeated build.' }
if ($env:TEMP -ne $env:TMP -or $env:TEMP -ne $env:TMPDIR -or $env:TEMP -ne $env:SAVETOOLKIT_TEST_TEMP) { throw 'Scratch was not isolated.' }
@{ Project = $project; Start = $start; End = [DateTime]::UtcNow; Temp = $env:TEMP
   Arguments = $args } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $results 'worker.json')
Set-Content -LiteralPath (Join-Path $results 'results.trx') -Value '<TestRun />'
if ($args -contains '--coverage') {
    Set-Content -LiteralPath $args[[array]::IndexOf($args, '--coverage-output') + 1] -Value '<coverage />'
}
Write-Output "OUTPUT $project"
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
Write-Output 'UNICODE ü_日本'
if ($project -eq 'Second.Tests') { exit 17 }
exit 0
'@ | Set-Content -LiteralPath $fakeScript
    if ($IsWindows) {
        $pwsh = (Get-Process -Id $PID).Path
        Set-Content -LiteralPath (Join-Path $fakeBin 'dotnet.cmd') -Value "@echo off`r`n`"$pwsh`" -NoProfile -File `"$fakeScript`" %*"
    }
    else {
        $shim = Join-Path $fakeBin 'dotnet'
        Set-Content -LiteralPath $shim -Value ("#!/usr/bin/env pwsh`n" + (Get-Content -LiteralPath $fakeScript -Raw)) -NoNewline
        & chmod +x $shim
    }
    $savedPath = $env:PATH
    $savedTemp, $savedTmp, $savedTmpDir = $env:TEMP, $env:TMP, $env:TMPDIR
    $results = Join-Path $fixture 'results space'
    $env:SAVETOOLKIT_TEST_TEMP = Join-Path $fixture 'scratch space'
    $output = [Collections.Generic.List[string]]::new()
    $errorText = ''
    try {
        $env:PATH = $fakeBin + [IO.Path]::PathSeparator + $savedPath
        try {
            & (Join-Path $fixture 'scripts/test.ps1') -MaxParallel 2 -Coverage -ResultsDirectory $results 6>&1 |
                ForEach-Object { $output.Add("$_") }
        }
        catch { $errorText = "$_" }
    }
    finally { $env:PATH = $savedPath }
    Assert-True ($errorText -match 'Failing test projects: Second.Tests.csproj') "Parallel failures must be aggregated: $errorText"
    $workers = @(Get-ChildItem -LiteralPath $results -Recurse -Filter 'worker.json' | ForEach-Object {
        Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
    })
    Assert-True ($workers.Count -eq 4) 'A failed worker must not prevent later projects running.'
    Assert-True (@($workers.Temp | Select-Object -Unique).Count -eq 4) 'Every worker needs distinct scratch.'
    $first = $workers | Where-Object Project -eq 'First.Tests'
    $fourth = $workers | Where-Object Project -eq 'Fourth.Tests'
    Assert-True ($first.Start -lt $fourth.End -and $fourth.Start -lt $first.End) 'Two test processes must actually overlap.'
    foreach ($worker in $workers) {
        $overlap = @($workers | Where-Object { $_.Start -le $worker.Start -and $_.End -gt $worker.Start }).Count
        Assert-True ($overlap -le 2) 'The configured parallelism bound must be respected.'
        Assert-True (!(Test-Path -LiteralPath $worker.Temp)) 'Workers must clean scratch even after a test fails.'
        $expectedThreads = [Math]::Max(1, [int][Math]::Floor([Environment]::ProcessorCount / 2))
        Assert-True ([int]$worker.Arguments[[array]::IndexOf($worker.Arguments, '--max-threads') + 1] -eq $expectedThreads) 'Parallel workers must share the CPU thread budget.'
        Assert-True (Test-Path -LiteralPath (Join-Path $results "$($worker.Project)/coverage.cobertura.xml")) 'Parallel coverage must retain isolated reports.'
    }
    $lines = @($output | Where-Object { $_ -like 'OUTPUT *' })
    Assert-True (($lines -join ',') -eq 'OUTPUT First.Tests,OUTPUT Fourth.Tests,OUTPUT Second.Tests,OUTPUT Third.Tests') 'Logs must be replayed in deterministic path order.'
    Assert-True (@($output | Where-Object { $_ -eq 'UNICODE ü_日本' }).Count -eq 4) 'Worker logs must preserve UTF-8 output.'
    $timings = Get-Content -LiteralPath (Join-Path $results 'project-timings.json') -Raw | ConvertFrom-Json
    Assert-True ($timings.Projects.Count -eq 4 -and $timings.MaxParallel -eq 2) 'Every worker needs a wall-time record.'
    Assert-True ($env:TEMP -eq $savedTemp -and $env:TMP -eq $savedTmp -and $env:TMPDIR -eq $savedTmpDir) 'Parallel runs must preserve parent temp settings.'
    Assert-True (@(Get-ChildItem -LiteralPath $env:SAVETOOLKIT_TEST_TEMP).Count -eq 0) 'Parallel runs must clean all invocation-owned scratch.'
    $filteredResults = Join-Path $fixture 'filtered results'
    try {
        $env:PATH = $fakeBin + [IO.Path]::PathSeparator + $savedPath
        & (Join-Path $fixture 'scripts/test.ps1') -Filter Third -MaxParallel 2 -ResultsDirectory $filteredResults
    }
    finally { $env:PATH = $savedPath }
    $filtered = @(Get-ChildItem -LiteralPath $filteredResults -Recurse -Filter 'worker.json')
    Assert-True ($filtered.Count -eq 1 -and $filtered[0].Directory.Name -eq 'Third.Tests') 'A partial batch must honor the project filter.'
    $graph = Get-Content -LiteralPath (Join-Path $fixture 'build.slnx') -Raw
    Assert-True ($graph.Contains('SaveToolkit.Api.csproj') -and $graph.Contains('Third.Tests.csproj') -and
        !$graph.Contains('First.Tests.csproj')) 'A filtered build must still build the API, without unrelated test projects.'
}

Invoke-Case 'test runner rejects invalid parallelism before building' {
    foreach ($degree in @(0, -1, 33)) {
        $rejected = $false
        try { & (Join-Path $sourceRoot 'scripts/test.ps1') -MaxParallel $degree }
        catch { $rejected = $true }
        Assert-True $rejected 'The worker bound must be a positive, bounded integer.'
    }
}

foreach ($explicitResults in @($true, $false)) {
    Invoke-Case "coverage uses per-project Cobertura files and preserves results (explicit directory: $explicitResults)" {
        $fixture = New-Fixture
        foreach ($name in @('First', 'Second')) {
            $project = Join-Path $fixture "tests/$name/$name.Tests.csproj"
            New-Item -ItemType Directory -Path (Split-Path -Parent $project) -Force | Out-Null
            Set-Content -LiteralPath $project '<Project />'
        }
        New-Item -ItemType Directory -Path (Join-Path $fixture 'plugins') | Out-Null
        $env:SAVETOOLKIT_TEST_TEMP = Join-Path $fixture 'scratch space'
        $results = if ($explicitResults) { Join-Path $testRoot 'coverage results space' } else { Join-Path $fixture 'TestResults' }
        $calls = [Collections.Generic.List[object]]::new()
        function dotnet {
            if ($args[0] -eq 'test') {
                $calls.Add($args)
                $outputPath = $args[[array]::IndexOf($args, '--coverage-output') + 1]
                New-Item -ItemType Directory -Path (Split-Path -Parent $outputPath) -Force | Out-Null
                Set-Content -LiteralPath $outputPath '<coverage />'
            }
            $global:LASTEXITCODE = 0
        }
        $parameters = @{ Coverage = $true; Configuration = 'Release'; MaxParallel = 1 }
        if ($explicitResults) { $parameters.ResultsDirectory = $results }
        & (Join-Path $fixture 'scripts/test.ps1') @parameters
        Assert-True ($calls.Count -eq 2) 'Coverage must run every test project.'
        foreach ($arguments in $calls) {
            $projectName = [IO.Path]::GetFileNameWithoutExtension($arguments[[array]::IndexOf($arguments, '--project') + 1])
            $projectResults = Join-Path $results $projectName
            Assert-True ($arguments -contains '--coverage') 'MTP coverage must be enabled.'
            Assert-True ($arguments[[array]::IndexOf($arguments, '--coverage-output-format') + 1] -eq 'cobertura') 'Reports must use Cobertura.'
            Assert-True ($arguments[[array]::IndexOf($arguments, '--results-directory') + 1] -eq $projectResults) 'Results must be isolated per project.'
            Assert-True ($arguments -contains '--report-xunit-trx') 'Coverage must retain TRX reporting.'
            Assert-True ($arguments[[array]::IndexOf($arguments, '--configuration') + 1] -eq 'Release') 'Coverage must preserve the requested configuration.'
            $expectedOutput = Join-Path $projectResults 'coverage.cobertura.xml'
            Assert-True ($arguments[[array]::IndexOf($arguments, '--coverage-output') + 1] -eq $expectedOutput) 'Coverage output must use a stable per-project path.'
            Assert-True (Test-Path -LiteralPath $expectedOutput) 'Coverage results must survive scratch cleanup.'
        }
        Assert-True (@(Get-ChildItem -LiteralPath $env:SAVETOOLKIT_TEST_TEMP).Count -eq 0) 'Coverage runs must clean temporary data.'
    }
}

Invoke-Case 'test cleanup failures warn without hiding native test failures' {
    $fixture = New-Fixture
    $project = Join-Path $fixture 'tests/Example/Example.Tests.csproj'
    New-Item -ItemType Directory -Path (Split-Path -Parent $project), (Join-Path $fixture 'plugins') -Force | Out-Null
    Set-Content -LiteralPath $project '<Project />'
    $env:SAVETOOLKIT_TEST_TEMP = Join-Path $fixture 'test scratch'
    function dotnet { $global:LASTEXITCODE = if ($args[0] -eq 'test') { 17 } else { 0 } }
    function Remove-Item {
        param($LiteralPath, [switch]$Recurse, [switch]$Force, $ErrorAction)
        if ((Split-Path -Leaf $LiteralPath) -like 'run-*') { throw [IO.IOException]::new('Simulated locked directory.') }
        Microsoft.PowerShell.Management\Remove-Item -LiteralPath $LiteralPath -Recurse:$Recurse -Force:$Force -ErrorAction Stop
    }
    $warnings = [Collections.Generic.List[object]]::new()
    $errorText = ''
    try {
        & (Join-Path $fixture 'scripts/test.ps1') -MaxParallel 1 3>&1 | ForEach-Object {
            if ($_ -is [Management.Automation.WarningRecord]) { $warnings.Add($_) }
        }
    }
    catch { $errorText = "$_" }
    Assert-True ($errorText -match 'Failing test projects: Example.Tests.csproj') 'Cleanup must preserve the original test failure.'
    Assert-True (@($warnings | Where-Object { "$_" -match 'Could not remove test scratch.*Simulated locked directory' }).Count -gt 0) 'Locked scratch must be reported.'
}
