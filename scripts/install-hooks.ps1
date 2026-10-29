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

function Get-GitObjectId([string]$path) {
    $output = & git -C $script:repoRoot hash-object --no-filters -- $path 2>$null
    if ($LASTEXITCODE -ne 0 -or !$output) {
        throw "Git could not hash the hook entry: $path"
    }
    return ($output | Out-String).Trim()
}

$pythonCommand = Get-Command python3 -ErrorAction SilentlyContinue
if ($null -eq $pythonCommand) { $pythonCommand = Get-Command python -ErrorAction SilentlyContinue }
if ($null -eq $pythonCommand) { throw 'Python 3 is required to classify repository hook entries.' }

function Test-RegularFile([object]$entry, [string]$path) {
    if ($null -eq $entry -or $entry.PSIsContainer -or
        (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
        return $false
    }
    & $script:pythonCommand.Source -B -c 'import os, stat, sys; sys.exit(0 if stat.S_ISREG(os.lstat(sys.argv[1]).st_mode) else 1)' $path 2>$null
    return $LASTEXITCODE -eq 0
}

function Test-ToolkitWrapper([string]$path, [string]$expectedHash, [string]$expectedObjectId) {
    $entry = Get-ExistingEntry $path
    if (!(Test-RegularFile $entry $path)) { return $false }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $expectedHash) { return $false }
    return (Get-GitObjectId $path) -ceq $expectedObjectId
}

function Test-ActivationMode([string]$sourcePath, [string]$targetPath) {
    if ($IsWindows) { return $true }
    try {
        return [IO.File]::GetUnixFileMode($sourcePath) -eq [IO.File]::GetUnixFileMode($targetPath)
    }
    catch { return $false }
}

$sourceEntry = Get-ExistingEntry $source
if (!(Test-RegularFile $sourceEntry $source)) { throw "Missing or unsupported checked-in hook: $source" }
$committedObjectId = (& git -C $repoRoot rev-parse --verify 'HEAD:.githooks/pre-commit' 2>$null | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or !$committedObjectId) {
    throw 'The committed hook blob HEAD:.githooks/pre-commit is unavailable.'
}
$workingTreeObjectId = Get-GitObjectId $source
if ($workingTreeObjectId -cne $committedObjectId) {
    throw "The working-tree hook differs from the committed wrapper; restore $source before installing."
}

$targetEntry = Get-ExistingEntry $target
$backupEntry = Get-ExistingEntry $original
$targetExists = $null -ne $targetEntry
$backupExists = $null -ne $backupEntry
$targetRegular = Test-RegularFile $targetEntry $target
$backupRegular = Test-RegularFile $backupEntry $original
if (($targetExists -and !$targetRegular) -or ($backupExists -and !$backupRegular)) {
    throw "Unsupported filesystem entry at '$target' or '$original'; inspect it before installing."
}
$sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
if (!$targetExists -and $backupExists) {
    throw "An existing hook backup at '$original' cannot be tied to a verified installation; inspect it before installing."
}
$targetIsWrapper = Test-ToolkitWrapper $target $sourceHash $committedObjectId
if ($targetExists -and !$targetIsWrapper) {
    if ($backupExists) {
        throw "An existing chained hook is already present at '$original'; remove it only after reviewing it."
    }
}

if ($targetIsWrapper) {
    if (!$PSCmdlet.ShouldProcess($target, 'keep the existing C# size pre-commit hook')) {
        Write-Host "What if: existing C# size guard already installed in $target"
        return
    }
    if (!(Test-ActivationMode $source $target)) {
        [IO.File]::SetUnixFileMode($target, [IO.File]::GetUnixFileMode($source))
        Write-Host "Restored executable mode on existing C# size guard in $target"
    }
    else {
        Write-Host "C# size guard already installed in $target"
    }
    return
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
    if (!(Test-ToolkitWrapper $temporary $sourceHash $committedObjectId)) { throw 'The prepared hook does not match the checked-in wrapper.' }

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
