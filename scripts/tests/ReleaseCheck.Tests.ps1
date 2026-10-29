. (Join-Path $PSScriptRoot 'ReleaseAutomation.Helpers.ps1')

Invoke-Case 'release check verifies exact HEAD CI and leaves repository and caller untouched' {
    $fixture = New-ReleaseFixture
    $calls = [Collections.Generic.List[object]]::new()
    function gh {
        Assert-True ((Get-Location).Path -eq $fixture) 'CI must query the checkout containing the scripts.'
        $calls.Add($args); $global:LASTEXITCODE = 0; Get-SuccessfulReleaseRun
    }
    $before = Invoke-FixtureGit rev-parse HEAD
    Set-Location -LiteralPath $testRoot
    & "$fixture/scripts/release-check.ps1" -Version '0.1.0-rc.3'
    Assert-True ($calls.Count -eq 1) 'Exactly one read-only CI query is needed.'
    $arguments = $calls[0]
    foreach ($expected in @('run', 'list', '--workflow', 'ci.yml', '--commit', "$before", '--branch', 'main', '--event', 'push', '--json')) {
        Assert-True ($arguments -contains $expected) "Missing exact-HEAD main CI query argument $expected."
    }
    Assert-True ((Get-Location).Path -eq $testRoot) 'Check must preserve caller location.'
    Set-Location -LiteralPath $fixture
    Assert-True ((Invoke-FixtureGit rev-parse HEAD) -eq $before -and !(Invoke-FixtureGit status --porcelain)) 'Check must leave git untouched.'
}

foreach ($state in @('branch', 'detached', 'untracked', 'unstaged', 'staged', 'tag')) {
    Invoke-Case "release check rejects unsafe release state ($state)" {
        $fixture = New-ReleaseFixture
        function gh { throw 'Unexpected external query.' }
        $pattern = switch ($state) {
            'branch' { Invoke-FixtureGit checkout --quiet -b topic | Out-Null; 'main' }
            'detached' { Invoke-FixtureGit checkout --quiet --detach | Out-Null; 'main' }
            'untracked' { Write-ReleaseFixtureFile "$fixture/extra.txt" 'extra'; 'clean' }
            'unstaged' { Add-Content "$fixture/Directory.Build.props" ' '; 'clean' }
            'staged' { Add-Content "$fixture/Directory.Build.props" ' '; Invoke-FixtureGit add --all | Out-Null; 'clean' }
            'tag' { Invoke-FixtureGit tag v0.1.0-rc.3 | Out-Null; 'already exists' }
        }
        Assert-ReleaseFailure { & "$fixture/scripts/release-check.ps1" -Version '0.1.0-rc.3' } $pattern
    }
}

foreach ($marker in @('missing', 'empty', 'DRAFT', 'TODO', 'TBD', 'FIXME', 'PLACEHOLDER', 'XXX', '<!-- draft -->', 'wrong title')) {
    Invoke-Case "release check rejects unfinished curated notes ($marker)" {
        $fixture = New-ReleaseFixture
        $path = "$fixture/docs/release-notes/v0.1.0-rc.3.md"
        if ($marker -eq 'missing') { Remove-Item -LiteralPath $path }
        elseif ($marker -eq 'empty') { [IO.File]::WriteAllText($path, '') }
        elseif ($marker -eq 'wrong title') { [IO.File]::WriteAllText($path, '# v0.1.0-rc.2 highlights') }
        else { Add-Content -LiteralPath $path "`n$marker" }
        Save-ReleaseFixture
        function gh { throw 'Unexpected external query.' }
        Assert-ReleaseFailure { & "$fixture/scripts/release-check.ps1" -Version '0.1.0-rc.3' } 'notes|placeholder'
    }
}

foreach ($fault in @('summary', 'appendix', 'status', 'status case', 'duplicate', 'missing')) {
    Invoke-Case "release check verifies SDK request counts ($fault)" {
        $fixture = New-ReleaseFixture
        $path = "$fixture/docs/SDK_REQUESTS.md"
        $text = Get-Content -LiteralPath $path -Raw
        $text = switch ($fault) {
            'summary' { $text.Replace('1 open', '2 open') }
            'appendix' { $text.Replace('The 1 resolved rows', 'The 2 resolved rows') }
            'status' { $text.Replace('| open |', '| unknown |') }
            'status case' { $text.Replace('| open |', '| Open |') }
            'duplicate' { $text.Replace('| SDK-04 |', '| SDK-01 |') }
            'missing' { $text.Replace('4 deduplicated items:', 'Inventory:') }
        }
        [IO.File]::WriteAllText($path, $text)
        Save-ReleaseFixture
        function gh { throw 'Unexpected external query.' }
        Assert-ReleaseFailure { & "$fixture/scripts/release-check.ps1" -Version '0.1.0-rc.3' } 'SDK_REQUESTS'
    }
}

foreach ($fault in @('prefix', 'reference', 'central package', 'empty package pin', 'version pin', 'targets pin', 'targets prefix')) {
    Invoke-Case "release check enforces tag-derived application version ($fault)" {
        $fixture = New-ReleaseFixture
        $path = "$fixture/src/SaveToolkit.Api/SaveToolkit.Api.csproj"
        $text = Get-Content -LiteralPath $path -Raw
        switch ($fault) {
            'prefix' { [IO.File]::WriteAllText($path, $text.Replace('<MinVerTagPrefix>v', '<MinVerTagPrefix>release-')) }
            'reference' { [IO.File]::WriteAllText($path, $text.Replace('Include="MinVer"', 'Include="Other"')) }
            'central package' { [IO.File]::WriteAllText("$fixture/Directory.Packages.props", '<Project />') }
            'empty package pin' { [IO.File]::WriteAllText("$fixture/Directory.Packages.props", '<Project><ItemGroup><PackageVersion Include="MinVer" /></ItemGroup></Project>') }
            'version pin' { [IO.File]::WriteAllText($path, $text.Replace('<RootNamespace>', '<Version>0.1.0-rc.2</Version><RootNamespace>')) }
            'targets pin' { Write-ReleaseFixtureFile "$fixture/Directory.Build.targets" '<Project><PropertyGroup><VersionPrefix>0.1.0</VersionPrefix></PropertyGroup></Project>' }
            'targets prefix' { Write-ReleaseFixtureFile "$fixture/Directory.Build.targets" '<Project><PropertyGroup><MinVerTagPrefix>release-</MinVerTagPrefix></PropertyGroup></Project>' }
        }
        Save-ReleaseFixture
        function gh { throw 'Unexpected external query.' }
        Assert-ReleaseFailure { & "$fixture/scripts/release-check.ps1" -Version '0.1.0-rc.3' } 'MinVer|version'
    }
}

foreach ($state in @('missing', 'failure', 'pending', 'wrong SHA', 'query failure', 'invalid JSON', 'newer failure', 'newer success')) {
    Invoke-Case "release check requires the latest successful HEAD CI ($state)" {
        $fixture = New-ReleaseFixture
        $head = Invoke-FixtureGit rev-parse HEAD
        $run = @{ databaseId = 10; status = 'completed'; conclusion = 'success'; headSha = "$head" }
        $runs = @($run)
        switch ($state) {
            'missing' { $runs = @() }
            'failure' { $run.conclusion = 'failure' }
            'pending' { $run.status = 'in_progress'; $run.conclusion = '' }
            'wrong SHA' { $run.headSha = 'wrong' }
            'newer failure' { $runs += @{ databaseId = 11; status = 'completed'; conclusion = 'failure'; headSha = "$head" } }
            'newer success' { $run.conclusion = 'failure'; $runs += @{ databaseId = 11; status = 'completed'; conclusion = 'success'; headSha = "$head" } }
        }
        function gh {
            $global:LASTEXITCODE = if ($state -eq 'query failure') { 1 } else { 0 }
            if ($state -eq 'invalid JSON') { return 'invalid' }
            return ConvertTo-Json -InputObject $runs -Compress
        }
        if ($state -eq 'newer success') { & "$fixture/scripts/release-check.ps1" -Version '0.1.0-rc.3' }
        else { Assert-ReleaseFailure { & "$fixture/scripts/release-check.ps1" -Version '0.1.0-rc.3' } 'CI|gh' }
    }
}

Invoke-Case 'release check warns only when gh is unavailable' {
    $fixture = New-ReleaseFixture
    function Get-Command {
        param([string]$Name, $ErrorAction)
        if ($Name -ne 'gh') { Microsoft.PowerShell.Core\Get-Command -Name $Name -ErrorAction $ErrorAction }
    }
    $warnings = @(& "$fixture/scripts/release-check.ps1" -Version '0.1.0-rc.3' 3>&1 |
        Where-Object { $_ -is [Management.Automation.WarningRecord] })
    Assert-True ($warnings.Count -eq 1 -and "$($warnings[0])" -match 'gh.*unavailable.*CI') 'Unavailable gh needs an explicit CI skip warning.'
}
