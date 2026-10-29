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
        $calls = [Collections.Generic.List[object]]::new()
        function dotnet {
            $calls.Add(@{ Arguments = $args; Temp = $env:TEMP; Tmp = $env:TMP; TmpDir = $env:TMPDIR; DotNetTemp = [IO.Path]::GetTempPath() })
            $global:LASTEXITCODE = if (($failure -eq 'build' -and $args[0] -eq 'build') -or
                ($failure -eq 'test' -and $args -match 'First.Tests.csproj')) { 17 } else { 0 }
        }
        $errorText = ''
        try { & (Join-Path $fixture 'scripts/test.ps1') -ResultsDirectory (Join-Path $testRoot 'results space') }
        catch { $errorText = "$_" }
        if ($failure -eq 'none') { Assert-True (!$errorText) "Unexpected failure: $errorText" }
        else { Assert-True ([bool]$errorText) 'A native failure must fail the script.' }
        Assert-True ($calls.Count -eq $(if ($failure -eq 'build') { 1 } else { 3 })) 'Test failures must not prevent later projects running.'
        foreach ($call in $calls) {
            Assert-True ($call.Temp -eq $call.Tmp -and $call.Temp -eq $call.TmpDir) 'TEMP, TMP and TMPDIR must match.'
            Assert-True ([IO.Path]::TrimEndingDirectorySeparator($call.DotNetTemp) -eq $call.Temp) '.NET must actually select the isolated temp directory.'
            Assert-True ([IO.Path]::IsPathFullyQualified($call.Temp)) 'Child scratch must be absolute.'
            Assert-True ((Split-Path -Parent $call.Temp) -eq (Join-Path $caller 'isolated scratch')) 'Relative scratch must resolve from the caller.'
            Assert-True (!(Test-Path -LiteralPath $call.Temp)) 'Child scratch must be removed after success/failure.'
        }
        Assert-True ($env:TEMP -eq $savedTemp -and $env:TMP -eq $savedTmp -and $env:TMPDIR -eq 'original Unix temp') 'All temp variables must be restored.'
        Assert-True ((Get-Location).Path -eq $caller) 'Caller location must be restored.'
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
        & (Join-Path $fixture 'scripts/test.ps1') 3>&1 | ForEach-Object {
            if ($_ -is [Management.Automation.WarningRecord]) { $warnings.Add($_) }
        }
    }
    catch { $errorText = "$_" }
    Assert-True ($errorText -match 'Failing test projects: Example.Tests.csproj') 'Cleanup must preserve the original test failure.'
    Assert-True (@($warnings | Where-Object { "$_" -match 'Could not remove test scratch.*Simulated locked directory' }).Count -gt 0) 'Locked scratch must be reported.'
}
