<#
.SYNOPSIS
Installs the repository's opt-in C# size pre-commit hook.

The installer changes only the repository's active hook directory. It never writes
user or global Git configuration. An existing pre-commit hook is moved to a sibling
*.savetoolkit-original file and invoked after the size check.
#>
[CmdletBinding(SupportsShouldProcess)]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (& git rev-parse --show-toplevel 2>$null).Trim()
if ($LASTEXITCODE -ne 0 -or !$repoRoot) { throw 'Run this script from inside a Git repository.' }
$repoRoot = [IO.Path]::GetFullPath($repoRoot)
$hookDirectory = (& git -C $repoRoot rev-parse --git-path hooks 2>$null).Trim()
if ($LASTEXITCODE -ne 0 -or !$hookDirectory) { throw 'Git did not return the active hooks directory.' }
if (![IO.Path]::IsPathRooted($hookDirectory)) { $hookDirectory = Join-Path $repoRoot $hookDirectory }
$hookDirectory = [IO.Path]::GetFullPath($hookDirectory)
New-Item -ItemType Directory -Path $hookDirectory -Force | Out-Null
$source = Join-Path $repoRoot '.githooks/pre-commit'
$target = Join-Path $hookDirectory 'pre-commit'
$original = Join-Path $hookDirectory 'pre-commit.savetoolkit-original'
if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing checked-in hook: $source" }

if ((Test-Path -LiteralPath $target -PathType Leaf) -and
    ([IO.Path]::GetFullPath($target) -ne [IO.Path]::GetFullPath($source))) {
    if (Test-Path -LiteralPath $original -PathType Leaf) {
        throw "An existing chained hook is already present at '$original'; remove it only after reviewing it."
    }
    if ($PSCmdlet.ShouldProcess($target, 'preserve existing pre-commit hook as a chained hook')) {
        Move-Item -LiteralPath $target -Destination $original
    }
}
if ($PSCmdlet.ShouldProcess($target, 'install C# size pre-commit hook')) {
    Copy-Item -LiteralPath $source -Destination $target -Force
}
Write-Host "Installed staged C# size guard in $target"
if (Test-Path -LiteralPath $original -PathType Leaf) {
    Write-Host "Preserved existing hook chain at $original"
}
