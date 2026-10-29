. (Join-Path $PSScriptRoot 'ReleaseAutomation.Helpers.ps1')

Invoke-Case 'release notes group first-parent merges and flag SDK changes, preserving unknown subjects' {
    $fixture = New-ReleaseFixture
    $sdk = Add-ReleaseMerge 'merge: fix (xorjson): preserve Yōtei bytes (#40)' 'src/SaveToolkit.Sdk/Contract.cs'
    $requests = Add-ReleaseMerge 'feat(api)!: changed contract (#41)' 'docs/SDK_REQUESTS.md'
    Add-ReleaseMerge 'Merge pull request #42 from example/topic' | Out-Null
    # A direct first-parent commit is intentionally outside the merged-work inventory.
    Write-ReleaseFixtureFile (Join-Path $fixture 'direct.txt') 'direct'
    Save-ReleaseFixture 'fix: direct commit must not appear'
    $caller = Join-Path $fixture 'caller space'
    New-Item -ItemType Directory -Path $caller | Out-Null
    Set-Location -LiteralPath $caller
    & (Join-Path $fixture 'scripts/release-notes.ps1') -Version '0.1.0-rc.3' -Force
    $text = Get-Content -LiteralPath (Join-Path $fixture 'docs/release-notes/v0.1.0-rc.3.md') -Raw
    foreach ($heading in @('Safer saves', 'New edit workflows', 'Faster', 'Editor', 'For plugin authors',
            'Packaging and CI', 'Known limitations', 'Checksums', 'Merged work (draft inventory)')) {
        Assert-True ($text.Contains("## $heading")) "Missing section $heading."
    }
    foreach ($expected in @('### fix', '#### xorjson', '### feat', '#### api', '**BREAKING**',
            '### other', 'Merge pull request #42', 'preserve Yōtei bytes', "commit/$sdk", "commit/$requests", 'TODO', 'DRAFT')) {
        Assert-True ($text.Contains($expected)) "Missing release inventory evidence: $expected"
    }
    $authors = ($text -split '## For plugin authors', 2)[1] -split '## Packaging and CI', 2 | Select-Object -First 1
    Assert-True ($authors.Contains("commit/$sdk") -and $authors.Contains("commit/$requests")) 'Both SDK paths must be flagged for plugin authors.'
    Assert-True (!$text.Contains('inner branch commit') -and !$text.Contains('direct commit must not appear')) 'Only first-parent merges belong in this inventory.'
    Assert-True ($text.Contains('v0.1.0-rc.2..HEAD')) 'Default base must be the latest reachable tag.'
    Assert-True ((Get-Location).Path -eq $caller) 'Generation must preserve caller location.'
}

Invoke-Case 'release notes protect curated bytes and require Force to replace them' {
    $fixture = New-ReleaseFixture
    $path = Join-Path $fixture 'docs/release-notes/v0.1.0-rc.3.md'
    $before = (Get-FileHash -LiteralPath $path).Hash
    Assert-ReleaseFailure { & "$fixture/scripts/release-notes.ps1" -Version '0.1.0-rc.3' } 'already exists'
    Assert-True ((Get-FileHash -LiteralPath $path).Hash -eq $before) 'Refusal must preserve curated bytes.'
    & "$fixture/scripts/release-notes.ps1" -Version '0.1.0-rc.3' -Force
    Assert-True ((Get-Content -LiteralPath $path -Raw).Contains('No merged work')) 'Empty merge ranges need an explicit inventory entry.'
}

Invoke-Case 'release notes use explicit previous tags and exclude older and unreachable work' {
    $fixture = New-ReleaseFixture
    Add-ReleaseMerge 'merge: fix(core): old change' | Out-Null
    Invoke-FixtureGit tag -a v0.1.0-rc.4 -m 'annotated release' | Out-Null
    Add-ReleaseMerge 'merge: perf (core): new change' | Out-Null
    Invoke-FixtureGit checkout --quiet --orphan unrelated | Out-Null
    Save-ReleaseFixture 'test: unrelated history'
    Invoke-FixtureGit tag v99.0.0 | Out-Null
    Invoke-FixtureGit checkout --quiet main | Out-Null
    & "$fixture/scripts/release-notes.ps1" -Version '0.1.0-rc.5'
    $path = Join-Path $fixture 'docs/release-notes/v0.1.0-rc.5.md'
    $text = Get-Content -LiteralPath $path -Raw
    Assert-True ($text.Contains('v0.1.0-rc.4..HEAD') -and !$text.Contains('old change') -and $text.Contains('new change')) 'Default must use reachable annotated tags, not unrelated higher tags.'
    & "$fixture/scripts/release-notes.ps1" -Version '0.1.0-rc.5' -PreviousTag 'v0.1.0-rc.2' -Force
    Assert-True ((Get-Content -LiteralPath $path -Raw).Contains('old change')) 'Explicit base must control the merge range.'
    Assert-ReleaseFailure { & "$fixture/scripts/release-notes.ps1" -Version '0.1.0-rc.6' -PreviousTag 'v99.0.0' } 'ancestor'
    Assert-ReleaseFailure { & "$fixture/scripts/release-notes.ps1" -Version '0.1.0-rc.6' -PreviousTag 'missing' } 'tag'
}

Invoke-Case 'release scripts reject invalid versions before creating files' {
    $fixture = New-ReleaseFixture
    foreach ($version in @('v0.1.0', '01.1.0', '0.1.0-rc.03', '0.1.0+meta', '../outside', '0.1.0-', '0.1.0/rc', "0.1.0`n")) {
        foreach ($script in @('release-notes', 'release-check')) {
            Assert-ReleaseFailure { & "$fixture/scripts/$script.ps1" -Version $version } 'SemVer'
        }
    }
    Assert-True (@(Get-ChildItem "$fixture/docs/release-notes").Count -eq 1) 'Invalid input must never create notes.'
}

Invoke-Case 'release notes exclude nested merges and retain breaking-change body markers' {
    $fixture = New-ReleaseFixture
    Invoke-FixtureGit checkout --quiet -b outer | Out-Null
    Invoke-FixtureGit checkout --quiet -b inner | Out-Null
    Write-ReleaseFixtureFile "$fixture/inner.txt" 'inner'
    Save-ReleaseFixture 'feat: inner'
    Invoke-FixtureGit checkout --quiet outer | Out-Null
    Invoke-FixtureGit -c commit.gpgsign=false merge --quiet --no-ff inner -m 'fix: nested merge must not appear' | Out-Null
    Invoke-FixtureGit checkout --quiet main | Out-Null
    Invoke-FixtureGit -c commit.gpgsign=false merge --quiet --no-ff outer -m 'merge: feat (sdk): new contract' -m 'BREAKING CHANGE: consumers must adapt' | Out-Null
    & "$fixture/scripts/release-notes.ps1" -Version '1.0.0'
    $text = Get-Content -LiteralPath "$fixture/docs/release-notes/v1.0.0.md" -Raw
    Assert-True (!$text.Contains('nested merge must not appear')) 'Nested merges must not leak into the first-parent inventory.'
    Assert-True ($text.Contains('**BREAKING** merge: feat (sdk): new contract')) 'Breaking-change merge bodies must remain visible.'
}

Invoke-Case 'release notes report missing tags and reject shallow history' {
    $fixture = New-ReleaseFixture
    Invoke-FixtureGit tag -d v0.1.0-rc.2 | Out-Null
    Assert-ReleaseFailure { & "$fixture/scripts/release-notes.ps1" -Version '0.1.0-rc.4' } 'tag'
    Invoke-FixtureGit tag v0.1.0-rc.2 | Out-Null
    $clone = Join-Path $testRoot 'shallow release repo'
    Invoke-FixtureGit clone --quiet --depth=1 --no-local $fixture $clone | Out-Null
    Assert-ReleaseFailure { & "$clone/scripts/release-notes.ps1" -Version '0.1.0-rc.4' } 'shallow'
}
