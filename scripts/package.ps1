<#
.SYNOPSIS
Builds the release archive of the browser app: the static site (UI plus the .NET WebAssembly
runtime and every plugin), licences and a checksum. It is platform-independent; no server is shipped.
.EXAMPLE
./scripts/package.ps1 -OutputDirectory D:/Temp/saveeditor/scratch/cicd/packages
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$SkipWebBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $output | Out-Null

function Invoke-Checked([string]$Command, [string[]]$Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE." }
}

Push-Location -LiteralPath $root
try {
    if (!$SkipWebBuild) {
        # The archive is served from the root of a local static server, never below a sub-path.
        $savedBase = $env:SAVETOOLKIT_WEB_BASE
        $env:SAVETOOLKIT_WEB_BASE = '/'
        Push-Location -LiteralPath (Join-Path $root 'web')
        try {
            Invoke-Checked npm @('ci', '--no-audit', '--no-fund')
            # Publishes src/SaveToolkit.Browser (needs the wasm-tools workload), then builds the UI.
            Invoke-Checked npm @('run', 'build')
        }
        finally {
            Pop-Location
            $env:SAVETOOLKIT_WEB_BASE = $savedBase
        }
    }
    $site = Join-Path $root 'web/dist'
    $index = Join-Path $site 'index.html'
    if (!(Test-Path -LiteralPath $index) -or !(Test-Path -LiteralPath (Join-Path $site '_framework/dotnet.js'))) {
        throw 'Build the static site (cd web; npm run build) before packaging.'
    }
    if ((Get-Content -LiteralPath $index -Raw) -notmatch 'src="/assets/') {
        throw 'The packaged site must be built with the root base path (unset SAVETOOLKIT_WEB_BASE).'
    }

    # The Api and the browser runtime share the same MinVer configuration, so this is the version
    # /api/version reports in the packaged app.
    $project = 'src/SaveToolkit.Api/SaveToolkit.Api.csproj'
    Invoke-Checked dotnet @('restore', $project)
    $versionOutput = & dotnet msbuild $project -target:MinVer -getProperty:MinVerVersion -verbosity:quiet
    if ($LASTEXITCODE -ne 0) { throw 'Could not resolve the git version.' }
    $version = ($versionOutput | Select-Object -Last 1).Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
        throw "Invalid build version: $version"
    }

    $name = "SaveToolkit-$version-web"
    # Only remove directories this invocation created. Never delete a caller-supplied output tree.
    $stage = Join-Path $output ("publish-" + [guid]::NewGuid().ToString('N'))
    $bundle = Join-Path $stage $name
    try {
        New-Item -ItemType Directory -Force -Path $bundle | Out-Null
        # Vite's .vite/manifest.json only serves the bundle-size check; it is not part of the site.
        Get-ChildItem -LiteralPath $site -Force | Where-Object Name -ne '.vite' |
            Copy-Item -Destination $bundle -Recurse -Force
        Copy-Item -LiteralPath (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD-PARTY-NOTICES') -Destination $bundle
        Copy-Item -LiteralPath (Join-Path $root 'docs/licenses') -Destination $bundle -Recurse
        @"
PS5 Save Game Toolkit $version (browser app)

Everything runs in your browser. No server, .NET or Node.js installation is needed, and
imported saves never leave your computer.

Browsers do not start WebAssembly apps from file:// pages, so serve this folder over HTTP
from the site root with any static web server. For example, with Python 3 run this inside
this folder (use python instead of python3 on Windows):
  python3 -m http.server 8080 --bind 127.0.0.1
then open http://127.0.0.1:8080/ in a current browser. Stop the server with Ctrl+C.

Workspaces live only in the open tab: export before closing or reloading it. Presets and
reference packs are kept in the browser's IndexedDB storage for this address.
Keep a separate backup of every save. Edits are experimental and not console-validated.
See LICENSE, THIRD-PARTY-NOTICES and licenses/ for redistribution terms.
Source and documentation: https://github.com/JKamsker/PS5SaveGame-Toolkit
"@ | Set-Content -LiteralPath (Join-Path $bundle 'README.txt') -Encoding utf8NoBOM
        $archive = Join-Path $output "$name.zip"
        # Compress-Archive also expands wildcards in DestinationPath's parent directory.
        # ZipFile treats every path literally and retains the top-level bundle folder.
        $stagedArchive = Join-Path $stage "$name.zip"
        [IO.Compression.ZipFile]::CreateFromDirectory($bundle, $stagedArchive,
            [IO.Compression.CompressionLevel]::Optimal, $true)
        Move-Item -LiteralPath $stagedArchive -Destination $archive -Force
        $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
        [IO.File]::WriteAllText("$archive.sha256", "$hash  $([IO.Path]::GetFileName($archive))`n")
        Write-Host "Created $archive"
    }
    finally {
        if (Test-Path -LiteralPath $stage) {
            $resolvedStage = (Resolve-Path -LiteralPath $stage).Path
            if ([IO.Path]::GetDirectoryName($resolvedStage) -ne [IO.Path]::TrimEndingDirectorySeparator($output)) {
                throw "Refusing to delete unexpected staging directory: $resolvedStage"
            }
            Remove-Item -LiteralPath $resolvedStage -Recurse -Force
        }
    }
}
finally { Pop-Location }
