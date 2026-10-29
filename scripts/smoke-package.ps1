<# .SYNOPSIS Launches an extracted bundle, verifies its version/UI, then removes the extraction. #>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PackageDirectory,
    [Parameter(Mandatory)][ValidateSet('win-x64', 'linux-x64', 'osx-arm64', 'osx-x64')][string]$Runtime,
    [Parameter(Mandatory)][string]$ScratchDirectory,
    [string]$ExpectedVersion = ''
)

$ErrorActionPreference = 'Stop'
$archives = @(Get-ChildItem -LiteralPath $PackageDirectory -File |
    Where-Object { $_.Name -like "*-$Runtime.zip" -or $_.Name -like "*-$Runtime.tar.gz" })
if ($archives.Count -ne 1) { throw "Expected exactly one archive for $Runtime." }
$archive = $archives[0]
$checksum = ((Get-Content -LiteralPath "$($archive.FullName).sha256" -Raw) -split '  ', 2)[0]
if ((Get-FileHash -LiteralPath $archive.FullName -Algorithm SHA256).Hash -ine $checksum) { throw 'Checksum mismatch.' }
$scratch = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($ScratchDirectory)
$extract = Join-Path $scratch ("smoke-" + [guid]::NewGuid().ToString('N'))
$stdout = "$extract.stdout.log"
$stderr = "$extract.stderr.log"
New-Item -ItemType Directory -Force -Path $extract | Out-Null
$process = $null
try {
    if ($archive.Extension -eq '.zip') { Expand-Archive -LiteralPath $archive.FullName -DestinationPath $extract }
    else {
        tar -xzf $archive.FullName -C $extract
        if ($LASTEXITCODE -ne 0) { throw 'Archive extraction failed.' }
    }
    $folders = @(Get-ChildItem -LiteralPath $extract -Directory)
    if ($folders.Count -ne 1) { throw 'Expected one top-level bundle folder.' }
    $bundle = $folders[0].FullName
    $executable = Join-Path $bundle $(if ($IsWindows) { 'SaveToolkit.Api.exe' } else { 'SaveToolkit.Api' })
    # Let the OS choose an unused loopback port, rather than conflicting with a running developer API.
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $listener.Stop()
    $url = "http://127.0.0.1:$port"
    $arguments = @('--urls', $url, '--contentRoot', "`"$bundle`"",
        '--Toolkit:DataRoot', "`"$(Join-Path $extract 'data')`"",
        '--Toolkit:PackRoot', "`"$(Join-Path $extract 'packs')`"")
    $start = @{
        FilePath = $executable; ArgumentList = $arguments; WorkingDirectory = $bundle; PassThru = $true
        RedirectStandardOutput = $stdout
        RedirectStandardError = $stderr
    }
    if ($IsWindows) { $start.WindowStyle = 'Hidden' }
    $process = Start-Process @start
    $version = $null
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($process.HasExited) { throw "Packaged server exited with $($process.ExitCode). See $stderr." }
        try { $version = Invoke-RestMethod "$url/api/version" -TimeoutSec 2; break }
        catch { Start-Sleep -Milliseconds 500 }
    }
    if (!$version.version) { throw 'Packaged server did not become ready.' }
    if ($ExpectedVersion -and ($version.version -split '\+', 2)[0] -cne $ExpectedVersion) {
        throw "Expected $ExpectedVersion, got $($version.version)."
    }
    $page = Invoke-WebRequest $url -TimeoutSec 10
    if ($page.StatusCode -ne 200 -or $page.Content -notmatch '<div id="root">') { throw 'Packaged UI did not load.' }
    $asset = [regex]::Match($page.Content, 'src="([^"]+\.js)"').Groups[1].Value
    if (!$asset) { throw 'UI has no JavaScript entry point.' }
    $javascript = Invoke-WebRequest "$url$asset" -TimeoutSec 10
    if ($javascript.StatusCode -ne 200 -or $javascript.Headers.'Content-Type' -notmatch 'javascript') {
        throw 'Packaged UI JavaScript did not load.'
    }
    Write-Host "Package smoke passed: $Runtime v$($version.version), UI and JavaScript served."
}
finally {
    if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
    $resolved = (Resolve-Path -LiteralPath $extract).Path
    if ([IO.Path]::GetDirectoryName($resolved) -ne [IO.Path]::TrimEndingDirectorySeparator($scratch)) {
        throw "Refusing to delete unexpected extraction directory: $resolved"
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
