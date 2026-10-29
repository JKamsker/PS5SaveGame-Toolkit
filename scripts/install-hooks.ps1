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

function Test-ToolkitWrapper([string]$path) {
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { return $false }
    $content = Get-Content -LiteralPath $path -Raw
    return $content.Contains('Optional repository hook installed by scripts/install-hooks.ps1.') -and
        $content.Contains('check_csharp_size.py')
}

$targetExists = Test-Path -LiteralPath $target -PathType Leaf
$targetIsWrapper = Test-ToolkitWrapper $target
if ($targetExists -and !$targetIsWrapper) {
    if (Test-Path -LiteralPath $original -PathType Leaf) {
        throw "An existing chained hook is already present at '$original'; remove it only after reviewing it."
    }
}
if (!$PSCmdlet.ShouldProcess($target, 'install C# size pre-commit hook')) {
    Write-Host "What if: would install staged C# size guard in $target"
    return
}

$temporary = Join-Path $hookDirectory ('.pre-commit.savetoolkit-' + [guid]::NewGuid().ToString('N') + '.tmp')
$movedOriginal = $false
try {
    # Prepare and validate the replacement while the existing hook is still active.
    Copy-Item -LiteralPath $source -Destination $temporary -Force
    if (!$IsWindows) {
        $mode = [IO.File]::GetUnixFileMode($source)
        [IO.File]::SetUnixFileMode($temporary, $mode)
    }
    if (!(Test-ToolkitWrapper $temporary)) { throw 'The prepared hook does not match the checked-in wrapper.' }

    if ($targetExists -and !$targetIsWrapper) {
        Move-Item -LiteralPath $target -Destination $original
        $movedOriginal = $true
    }
    Move-Item -LiteralPath $temporary -Destination $target -Force
    Write-Host "Installed staged C# size guard in $target"
}
catch {
    if ($movedOriginal -and !(Test-Path -LiteralPath $target -PathType Leaf) -and
        (Test-Path -LiteralPath $original -PathType Leaf)) {
        Move-Item -LiteralPath $original -Destination $target -Force -ErrorAction SilentlyContinue
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $temporary -PathType Leaf) {
        Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
    }
}
if (Test-Path -LiteralPath $original -PathType Leaf) {
    Write-Host "Preserved existing hook chain at $original"
}
