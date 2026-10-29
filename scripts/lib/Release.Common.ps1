# Shared, offline release inputs. Callers set releaseRoot to the checkout containing their scripts.
function Assert-ReleaseVersion([string]$Version) {
    $number = '(0|[1-9][0-9]*)'
    $identifier = '(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
    if ($Version -cnotmatch "\A$number\.$number\.$number(?:-$identifier(?:\.$identifier)*)?\z") {
        throw 'Expected SemVer without v or build metadata (for example 0.1.0-rc.3).'
    }
}

function Invoke-ReleaseGit {
    $output = & git -C $releaseRoot @args 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed: $($output -join "`n")" }
    return $output
}

function Assert-ReleaseHistory {
    if ((Invoke-ReleaseGit rev-parse --is-shallow-repository) -ne 'false') {
        throw 'Release preparation requires full history; this is a shallow clone. Fetch full history and tags first.'
    }
}

function Get-ReleaseNotesPath([string]$Version) {
    return Join-Path $releaseRoot "docs/release-notes/v$Version.md"
}

function Get-ReleaseBase([string]$PreviousTag) {
    if (!$PreviousTag) {
        # First-parent ancestry selects the preceding release on this integration line, not an
        # unrelated higher version or a tag that is only reachable through a merged topic branch.
        $PreviousTag = Invoke-ReleaseGit describe --tags --match 'v*' --abbrev=0 --first-parent HEAD
    }
    $reference = "refs/tags/$PreviousTag"
    Invoke-ReleaseGit check-ref-format $reference | Out-Null
    $commit = Invoke-ReleaseGit rev-parse --verify --end-of-options "$reference^{commit}"
    & git -C $releaseRoot merge-base --is-ancestor $commit HEAD
    if ($LASTEXITCODE -ne 0) { throw "Previous tag $PreviousTag must be an ancestor of HEAD." }
    return @{ Tag = $PreviousTag; Commit = $commit }
}
