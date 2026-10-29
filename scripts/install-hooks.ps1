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
function Get-ExistingEntry([string]$path) {
    return Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
}

function Test-RegularFile([object]$entry) {
    return $null -ne $entry -and !$entry.PSIsContainer -and
        (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0)
}

function Test-ToolkitWrapper([string]$path, [string]$expectedHash) {
    $entry = Get-ExistingEntry $path
    if (!(Test-RegularFile $entry)) { return $false }
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ceq $expectedHash
}

$sourceEntry = Get-ExistingEntry $source
if (!(Test-RegularFile $sourceEntry)) { throw "Missing or unsupported checked-in hook: $source" }
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash

$targetEntry = Get-ExistingEntry $target
$backupEntry = Get-ExistingEntry $original
$targetExists = $null -ne $targetEntry
$backupExists = $null -ne $backupEntry
$targetRegular = Test-RegularFile $targetEntry
$backupRegular = Test-RegularFile $backupEntry
if (($targetExists -and !$targetRegular) -or ($backupExists -and !$backupRegular)) {
    throw "Unsupported filesystem entry at '$target' or '$original'; inspect it before installing."
}
$targetIsWrapper = Test-ToolkitWrapper $target $sourceHash
if ($targetExists -and !$targetIsWrapper) {
    if ($backupExists) {
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
    if (!(Test-ToolkitWrapper $temporary $sourceHash)) { throw 'The prepared hook does not match the checked-in wrapper.' }

    if ($targetExists -and !$targetIsWrapper) {
        Move-Item -LiteralPath $target -Destination $original
        $movedOriginal = $true
    }
    Move-Item -LiteralPath $temporary -Destination $target -Force
    Write-Host "Installed staged C# size guard in $target"
}
catch {
    if ($movedOriginal -and $null -eq (Get-ExistingEntry $target) -and
        $null -ne (Get-ExistingEntry $original)) {
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
