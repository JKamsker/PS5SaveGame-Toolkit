<# .SYNOPSIS Checks a committed main revision before creating a release tag; never tags or publishes. #>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Version)

$ErrorActionPreference = 'Stop'
$releaseRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'lib/Release.Common.ps1')
. (Join-Path $PSScriptRoot 'lib/Release.Checks.ps1')
Assert-ReleaseVersion $Version
Assert-ReleaseHistory
if ((Invoke-ReleaseGit rev-parse --abbrev-ref HEAD) -cne 'main') { throw 'Release check requires the main branch (not detached HEAD).' }
if (Invoke-ReleaseGit status --porcelain=v1 --untracked-files=all) { throw 'Release check requires a clean working tree, including staged and untracked files.' }
if (Invoke-ReleaseGit tag --list "v$Version") { throw "Tag v$Version already exists." }
Assert-ReleaseVersionConfiguration
Assert-CuratedReleaseNotes $Version
Assert-SdkRequestCounts
$head = Invoke-ReleaseGit rev-parse HEAD
Assert-ReleaseCi $head
Write-Host "Release checks passed for v$Version at $head. No tag or release was created."
