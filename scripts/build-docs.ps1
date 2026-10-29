<#
.SYNOPSIS
  Builds and validates the public documentation site, with generated SDK and HTTP references.
.EXAMPLE
  ./scripts/build-docs.ps1 -ScratchDirectory D:/Temp/saveeditor/test-scratch/docs-site
#>
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$ScratchDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$scratch = [IO.Path]::GetFullPath($ScratchDirectory)
$rootPrefix = $root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($scratch.Equals($root, [StringComparison]::OrdinalIgnoreCase) -or
    $scratch.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Choose scratch outside the checkout; generated output must not be mixed with source.'
}
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
# Never delete an earlier run; each build owns its own directory and host roots.
$run = Join-Path $scratch ('build-' + [Guid]::NewGuid().ToString('N'))
$stage = Join-Path $run 'source'
$dependencies = Join-Path $scratch 'dependencies'
$environmentNames = @('TEMP', 'TMP', 'TMPDIR', 'SAVETOOLKIT_TEST_TEMP', 'SAVETOOLKIT_ARTIFACTS',
    'NUGET_PACKAGES', 'DOTNET_CLI_HOME', 'npm_config_cache', 'DOTNET_NOLOGO', 'DOTNET_CLI_TELEMETRY_OPTOUT')
$savedEnvironment = @{}
foreach ($name in $environmentNames) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }

function Invoke-DocsCommand {
    param([string]$Command, [string[]]$Arguments)
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE." }
}

try {
    New-Item -ItemType Directory -Force -Path $run, $stage, $dependencies, "$scratch/empty-samples" | Out-Null
    $env:TEMP = $env:TMP = $env:TMPDIR = $run
    $env:SAVETOOLKIT_TEST_TEMP = Join-Path $run 'tests'
    $env:SAVETOOLKIT_ARTIFACTS = Join-Path $scratch 'empty-samples'
    # The docs workflow configures setup-dotnet's cache to this same scratch location.
    $env:NUGET_PACKAGES = Join-Path $scratch 'nuget-packages'
    $env:DOTNET_CLI_HOME = Join-Path $scratch 'dotnet-home'
    $env:npm_config_cache = Join-Path $scratch 'npm-cache'
    $env:DOTNET_NOLOGO = 'true'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = 'true'
    Push-Location $root
    try {
        $python = if (Get-Command python3 -ErrorAction SilentlyContinue) { 'python3' } else { 'python' }
        # Only explicitly curated public source is staged. Never copy the repository or artifacts.
        Invoke-DocsCommand $python @('-B', 'scripts/docs/prepare_site.py', '--destination', $stage)
        Copy-Item -LiteralPath 'docs-site/package.json', 'docs-site/package-lock.json' -Destination $dependencies
        Invoke-DocsCommand npm @('ci', '--prefix', $dependencies, '--ignore-scripts', '--no-audit', '--no-fund')
        Invoke-DocsCommand $python @('-B', 'scripts/docs/bundle_renderer.py', '--package', "$dependencies/node_modules/redoc", '--stage', $stage)
        Invoke-DocsCommand dotnet @('tool', 'restore')
        $artifacts = Join-Path $run 'dotnet'
        Invoke-DocsCommand dotnet @('build', 'src/SaveToolkit.Api/SaveToolkit.Api.csproj', '-c', 'Release', '--artifacts-path', $artifacts)
        New-Item -ItemType Directory -Force -Path "$stage/sdk-input" | Out-Null
        Copy-Item -Path "$artifacts/bin/SaveToolkit.Sdk/release/SaveToolkit.Sdk.*" -Destination "$stage/sdk-input"
        . "$PSScriptRoot/lib/Docs.OpenApi.ps1"
        Export-DocsOpenApi -Assembly "$artifacts/bin/SaveToolkit.Api/release/SaveToolkit.Api.dll" `
            -ScratchDirectory $run -Destination "$stage/reference/openapi.json"
        Invoke-DocsCommand $python @('-B', 'scripts/docs/check_schema.py', '--schema', "$stage/reference/openapi.json")
        Invoke-DocsCommand dotnet @('tool', 'run', 'docfx', '--', "$stage/docfx.json", '--warningsAsErrors')
        Invoke-DocsCommand $python @('-B', 'scripts/docs/check_site.py', '--site', "$stage/_site")
        Invoke-DocsCommand $python @('-B', 'scripts/docs/check_routes.py', '--site', "$stage/_site")
        $site = Join-Path $stage '_site'
        Set-Content -LiteralPath "$scratch/latest-site.txt" -Value $site -Encoding utf8
        Write-Host "Documentation site: $site"
        Write-Host "Preview: $python -m http.server 8080 --bind 127.0.0.1 --directory `"$site`""
    }
    finally { Pop-Location }
}
finally {
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
}
