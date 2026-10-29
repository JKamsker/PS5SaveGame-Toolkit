<#
.SYNOPSIS
Builds self-contained release bundles with the UI, native SQLite, licences and checksums.
.EXAMPLE
./scripts/package.ps1 -Runtime win-x64 -OutputDirectory D:/Temp/saveeditor/scratch/cicd/packages
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'osx-arm64', 'osx-x64')]
    [string[]]$Runtime = @('win-x64', 'linux-x64', 'osx-arm64', 'osx-x64'),
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$SkipWebBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null

function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE." }
}

Push-Location $root
try {
    if (!$SkipWebBuild) {
        Push-Location (Join-Path $root 'web')
        try {
            Invoke-Checked npm @('ci', '--no-audit', '--no-fund')
            Invoke-Checked npm @('run', 'build')
        }
        finally { Pop-Location }
    }
    $webRoot = Join-Path $root 'src/SaveToolkit.Api/wwwroot'
    if (!(Test-Path (Join-Path $webRoot 'index.html'))) { throw 'Build the web UI before packaging.' }

    $project = 'src/SaveToolkit.Api/SaveToolkit.Api.csproj'
    Invoke-Checked dotnet @('restore', $project)
    $versionOutput = & dotnet msbuild $project -target:MinVer -getProperty:MinVerVersion -verbosity:quiet
    if ($LASTEXITCODE -ne 0) { throw 'Could not resolve the git version.' }
    $version = ($versionOutput | Select-Object -Last 1).Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
        throw "Invalid build version: $version"
    }

    foreach ($rid in $Runtime) {
        $name = "SaveToolkit-$version-$rid"
        # Only remove directories this invocation created. Never delete a caller-supplied output tree.
        $stage = Join-Path $output ("publish-" + [guid]::NewGuid().ToString('N'))
        $bundle = Join-Path $stage $name
        try {
            Invoke-Checked dotnet @('publish', $project, '--configuration', 'Release', '--runtime', $rid,
                '--self-contained', 'true', '--output', $bundle, '--warnaserror',
                '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-p:UseAppHost=true')
            $nativeName = if ($rid.StartsWith('win-')) { 'e_sqlite3.dll' }
                elseif ($rid.StartsWith('osx-')) { 'libe_sqlite3.dylib' } else { 'libe_sqlite3.so' }
            foreach ($required in @('wwwroot/index.html', $nativeName, 'SaveToolkit.Api.deps.json')) {
                if (!(Test-Path (Join-Path $bundle $required))) { throw "Bundle $rid is missing $required." }
            }
            Copy-Item -LiteralPath (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD-PARTY-NOTICES') -Destination $bundle
            Copy-Item -LiteralPath (Join-Path $root 'docs/licenses') -Destination $bundle -Recurse
            @"
PS5 Save Game Toolkit $version ($rid)

Extract the entire archive, then run start.cmd (Windows) or ./start.sh (macOS/Linux).
Open http://127.0.0.1:5180 in your browser. Stop with Ctrl+C.
No .NET or Node.js installation is needed. Unix: run chmod +x start.sh SaveToolkit.Api if needed.
The server binds to loopback and has no authentication. Keep it on your own computer.
Workspaces/reference packs are stored in ./data beside this file by the launch script.
Keep a separate backup of every save. Edits are experimental and not console-validated.
See LICENSE, THIRD-PARTY-NOTICES and licenses/ for redistribution terms.
Source and documentation: https://github.com/JKamsker/PS5SaveGame-Toolkit
"@ | Set-Content -LiteralPath (Join-Path $bundle 'README.txt') -Encoding utf8NoBOM
            if ($rid.StartsWith('win-')) {
                @'
@echo off
cd /d "%~dp0"
"%~dp0SaveToolkit.Api.exe" --urls http://127.0.0.1:5180 --contentRoot "%~dp0." --Toolkit:DataRoot "%~dp0data\workspaces" --Toolkit:PackRoot "%~dp0data\reference-packs"
'@ | Set-Content -LiteralPath (Join-Path $bundle 'start.cmd') -Encoding ascii
                $archive = Join-Path $output "$name.zip"
                Compress-Archive -Path $bundle -DestinationPath $archive -Force
            }
            else {
                $launcher = @'
#!/bin/sh
set -eu
cd "$(dirname "$0")"
exec ./SaveToolkit.Api --urls http://127.0.0.1:5180 --contentRoot "$PWD" --Toolkit:DataRoot "$PWD/data/workspaces" --Toolkit:PackRoot "$PWD/data/reference-packs"
'@
                [IO.File]::WriteAllText((Join-Path $bundle 'start.sh'), ($launcher -replace "`r`n", "`n") + "`n")
                if (!$IsWindows) { Invoke-Checked chmod @('+x', (Join-Path $bundle 'start.sh'), (Join-Path $bundle 'SaveToolkit.Api')) }
                else { Write-Warning 'Unix bundles built on Windows need chmod +x after extraction.' }
                $archive = Join-Path $output "$name.tar.gz"
                Invoke-Checked tar @('-czf', $archive, '-C', $stage, $name)
            }
            $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
            [IO.File]::WriteAllText("$archive.sha256", "$hash  $([IO.Path]::GetFileName($archive))`n")
            Write-Host "Created $archive"
        }
        finally {
            if (Test-Path -LiteralPath $stage) {
                $resolvedStage = (Resolve-Path -LiteralPath $stage).Path
                if ([IO.Path]::GetDirectoryName($resolvedStage) -ne $output.TrimEnd([IO.Path]::DirectorySeparatorChar)) {
                    throw "Refusing to delete unexpected staging directory: $resolvedStage"
                }
                Remove-Item -LiteralPath $resolvedStage -Recurse -Force
            }
        }
    }
}
finally { Pop-Location }
