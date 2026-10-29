<# .SYNOPSIS Generates an offline draft of curated highlights from first-parent merge commits. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$PreviousTag,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$releaseRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'lib/Release.Common.ps1')
Assert-ReleaseVersion $Version
Assert-ReleaseHistory
$path = Get-ReleaseNotesPath $Version
if ((Test-Path -LiteralPath $path) -and !$Force) {
    throw "Curated notes file already exists: $path. Use -Force to replace it with a draft."
}
$base = Get-ReleaseBase $PreviousTag
$commits = @(Invoke-ReleaseGit log --merges --first-parent --reverse '--format=%H%x09%s' "$($base.Commit)..HEAD")
$work = foreach ($commit in $commits) {
    $hash, $subject = $commit -split "`t", 2
    $type = 'other'
    $scope = 'general'
    $breaking = ''
    $conventional = $subject -replace '^merge:\s*', ''
    if ($conventional -match '^(?<type>[a-z]+)\s*(?:\((?<scope>[^)]+)\))?(?<breaking>!)?:\s*(?<description>.+)$') {
        $type = $Matches.type.ToLowerInvariant()
        if ($Matches.scope) { $scope = $Matches.scope.ToLowerInvariant() }
        if ($Matches.breaking) { $breaking = '**BREAKING** ' }
    }
    $body = (Invoke-ReleaseGit show -s --format=%B $hash) -join "`n"
    if ($body -match '(?m)^BREAKING[ -]CHANGE:\s*\S') { $breaking = '**BREAKING** ' }
    $sdkPaths = @(Invoke-ReleaseGit diff --name-only "$hash^1" $hash '--' 'src/SaveToolkit.Sdk' 'docs/SDK_REQUESTS.md')
    $link = "https://github.com/JKamsker/PS5SaveGame-Toolkit/commit/$hash"
    [pscustomobject]@{
        Type = $type; Scope = $scope; PluginAuthors = $sdkPaths.Count -gt 0
        Line = "- $breaking$subject ([$($hash.Substring(0, 8))]($link))"
    }
}
$work = @($work)
$inventory = [Collections.Generic.List[string]]::new()
foreach ($typeGroup in $work | Group-Object Type | Sort-Object Name) {
    $inventory.Add("### $($typeGroup.Name)")
    $inventory.Add('')
    foreach ($scopeGroup in $typeGroup.Group | Group-Object Scope | Sort-Object Name) {
        $inventory.Add("#### $($scopeGroup.Name)")
        $inventory.Add('')
        foreach ($item in $scopeGroup.Group) {
            $flag = if ($item.PluginAuthors) { ' **For plugin authors**' } else { '' }
            $inventory.Add($item.Line + $flag)
        }
        $inventory.Add('')
    }
}
if (!$work.Count) { $inventory.Add('- No merged work in this range.') }
$authors = @($work | Where-Object PluginAuthors | ForEach-Object Line)
if (!$authors.Count) { $authors = @('- TODO: Confirm whether other changes affect plugin authors.') }
$draft = @"
# v$Version highlights

**DRAFT — TODO: Curate the highlights below and remove all placeholder markers before release.**

This release builds on $($base.Tag).
**Edits remain experimental and are not console-validated. Keep an untouched backup.**

## Safer saves

- TODO: Summarize save robustness and data-preservation changes, with evidence links.

## New edit workflows

- TODO: Summarize new workflows and their evidence and write limits.

## Faster

- TODO: Summarize measured improvements, with setup, units and exclusions; remove if none.

## Editor

- TODO: Summarize editor changes.

## For plugin authors

$($authors -join "`n")

## Packaging and CI

- TODO: Summarize packaging and validation changes.

## Known limitations

- TODO: Recheck console acceptance, experimental writes and the current SDK request backlog.

## Checksums

- TODO: Confirm the release includes all four archives, adjacent .sha256 files and SHA256SUMS.
  Verify downloads with ``sha256sum --check SHA256SUMS`` (Linux),
  ``shasum -a 256 --check SHA256SUMS`` (macOS), or ``Get-FileHash -Algorithm SHA256`` (Windows).
  The manifest covers archives; checksum values are supplied by the packaging workflow.

## Merged work (draft inventory)

Range: $($base.Tag)..HEAD (first-parent merges only; direct commits are not listed).
TODO: Review every candidate, move relevant highlights into the sections above, and remove this inventory.

$($inventory -join "`n")
"@
New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
# CreateNew also protects curated files if another process creates one after the initial check.
$mode = if ($Force) { [IO.FileMode]::Create } else { [IO.FileMode]::CreateNew }
$stream = [IO.File]::Open($path, $mode, [IO.FileAccess]::Write, [IO.FileShare]::None)
try {
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($draft -replace "`r`n", "`n") + "`n")
    $stream.Write($bytes, 0, $bytes.Length)
}
finally { $stream.Dispose() }
Write-Output ($inventory -join "`n")
Write-Host "Draft written to $path; curate it before running release-check.ps1."
