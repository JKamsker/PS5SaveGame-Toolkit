<#
.SYNOPSIS
Verifies the browser-app archive offline: checksum, layout, entry assets, runtime and version, then
removes the extraction. The built app itself is exercised in a browser by the CI e2e job.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][string]$ScratchDirectory,
    [string]$ExpectedVersion = ''
)

$ErrorActionPreference = 'Stop'
$archives = @(Get-ChildItem -LiteralPath $PackageDirectory -File | Where-Object { $_.Name -like 'SaveToolkit-*-web.zip' })
if ($archives.Count -ne 1) { throw 'Expected exactly one SaveToolkit-<version>-web.zip archive.' }
$archive = $archives[0]
$checksum = ((Get-Content -LiteralPath "$($archive.FullName).sha256" -Raw) -split '  ', 2)[0]
if ((Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash -ine $checksum) { throw 'Checksum mismatch.' }
$scratch = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ScratchDirectory)
$extract = Join-Path $scratch ("smoke-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $extract | Out-Null
try {
    Expand-Archive -LiteralPath $archive.FullName -DestinationPath $extract
    $folders = @(Get-ChildItem -LiteralPath $extract -Directory)
    if ($folders.Count -ne 1 -or @(Get-ChildItem -LiteralPath $extract -File).Count -ne 0) { throw 'Expected one top-level bundle folder.' }
    $bundle = $folders[0].FullName
    foreach ($required in @('index.html', 'README.txt', 'LICENSE', 'THIRD-PARTY-NOTICES', 'licenses', '_framework/dotnet.js')) {
        if (!(Test-Path -LiteralPath (Join-Path $bundle $required))) { throw "Bundle is missing $required." }
    }
    $page = Get-Content -LiteralPath (Join-Path $bundle 'index.html') -Raw
    if ($page -notmatch '<div id="root">') { throw 'Packaged UI has no application root.' }
    # The archive is built for the site root, so every entry asset must resolve inside the bundle.
    $assets = @([regex]::Matches($page, '(?:src|href)="/(assets/[^"]+\.(?:js|css))"') | ForEach-Object { $_.Groups[1].Value })
    if (!($assets | Where-Object { $_ -like '*.js' })) { throw 'UI has no JavaScript entry point.' }
    foreach ($asset in $assets) {
        if (!(Test-Path -LiteralPath (Join-Path $bundle $asset))) { throw "Packaged UI asset is missing: $asset" }
    }
    $runtime = @(Get-ChildItem -LiteralPath (Join-Path $bundle '_framework') -File -Filter 'SaveToolkit.Browser*.wasm')
    if ($runtime.Count -ne 1) { throw 'Packaged runtime has no single SaveToolkit.Browser assembly.' }
    $reported = 'unchecked'
    if ($ExpectedVersion) {
        # /api/version reports the browser assembly's informational version: a length-prefixed custom
        # attribute string, ExpectedVersion plus optional +metadata, followed by two zero bytes.
        $text = [Text.Encoding]::Latin1.GetString([IO.File]::ReadAllBytes($runtime[0].FullName))
        $match = @([regex]::Matches($text, [regex]::Escape($ExpectedVersion) + '(?:\+[0-9A-Za-z.-]+)?(?=\x00\x00)') |
            Where-Object { $_.Index -gt 0 -and [int]$text[$_.Index - 1] -eq $_.Length } | Select-Object -First 1)
        if (!$match) { throw "Packaged runtime does not report version $ExpectedVersion." }
        $reported = $match[0].Value
    }
    Write-Host "Package smoke passed: v$reported, UI entry assets and .NET WebAssembly runtime present."
}
finally {
    $resolved = (Resolve-Path -LiteralPath $extract).Path
    if ([IO.Path]::GetDirectoryName($resolved) -ne [IO.Path]::TrimEndingDirectorySeparator($scratch)) {
        throw "Refusing to delete unexpected extraction directory: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
