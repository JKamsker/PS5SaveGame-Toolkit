function Invoke-FixtureGit {
    $output = & git @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Fixture git failed: $output" }
    return $output
}

function Write-ReleaseFixtureFile([string]$Path, [string]$Text) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [IO.File]::WriteAllText($Path, $Text)
}

function Save-ReleaseFixture([string]$Subject = 'test: fixture') {
    Invoke-FixtureGit add --all | Out-Null
    Invoke-FixtureGit -c commit.gpgsign=false commit --quiet -m $Subject | Out-Null
}

function New-ReleaseFixture {
    $fixture = New-Fixture
    Set-Location -LiteralPath $fixture
    Invoke-FixtureGit init --quiet --initial-branch=main | Out-Null
    Invoke-FixtureGit config user.name 'Script regression' | Out-Null
    Invoke-FixtureGit config user.email 'script-test@example.invalid' | Out-Null
    Invoke-FixtureGit config core.autocrlf false | Out-Null
    Invoke-FixtureGit config core.hooksPath (Join-Path $fixture 'no-hooks') | Out-Null
    Invoke-FixtureGit config tag.gpgsign false | Out-Null
    foreach ($path in @('Directory.Build.props', 'Directory.Packages.props', 'src/SaveToolkit.Api/SaveToolkit.Api.csproj')) {
        Write-ReleaseFixtureFile (Join-Path $fixture $path) (Get-Content -LiteralPath (Join-Path $sourceRoot $path) -Raw)
    }
    Write-ReleaseFixtureFile (Join-Path $fixture 'docs/SDK_REQUESTS.md') @'
# Requests

**4 deduplicated items: 1 open, 1 partially resolved, 1 resolved, 1 obsolete.**

## Prioritized inventory

| ID | Request | Requested by | Status | Evidence |
| --- | --- | --- | --- | --- |
| SDK-01 | A | Test | open | — |
| SDK-02 | B | Test | partially resolved | — |
| SDK-03 | C | Test | resolved | — |
| SDK-04 | D | Test | obsolete | — |

## Resolved appendix

The 1 resolved rows are delivered.
'@
    Write-ReleaseFixtureFile (Join-Path $fixture 'docs/release-notes/v0.1.0-rc.3.md') "# v0.1.0-rc.3 highlights`n`nReviewed release highlights.`n"
    Save-ReleaseFixture
    Invoke-FixtureGit tag v0.1.0-rc.2 | Out-Null
    return $fixture
}

function Add-ReleaseMerge([string]$Subject, [string]$Path = 'changes.txt') {
    $branch = 'work-' + [guid]::NewGuid().ToString('N')
    Invoke-FixtureGit checkout --quiet -b $branch | Out-Null
    Write-ReleaseFixtureFile (Join-Path (Get-Location).Path $Path) $branch
    Save-ReleaseFixture 'feat: inner branch commit must not appear separately'
    Invoke-FixtureGit checkout --quiet main | Out-Null
    Invoke-FixtureGit -c commit.gpgsign=false merge --quiet --no-ff $branch -m $Subject | Out-Null
    return (Invoke-FixtureGit rev-parse HEAD)
}

function Assert-ReleaseFailure([scriptblock]$Action, [string]$Pattern) {
    $message = ''
    try { & $Action | Out-Null }
    catch {
        if ($_.Exception -is [Management.Automation.CommandNotFoundException]) { throw }
        $message = "$_"
    }
    Assert-True ($message -match $Pattern) "Expected failure matching '$Pattern', got '$message'."
}

function Get-SuccessfulReleaseRun {
    $head = Invoke-FixtureGit rev-parse HEAD
    return @(@{ databaseId = 10; status = 'completed'; conclusion = 'success'; headSha = "$head" }) | ConvertTo-Json -AsArray
}
