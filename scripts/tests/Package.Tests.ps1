foreach ($failure in @('none', 'restore', 'msbuild', 'npm', 'base')) {
    Invoke-Case "packaging checks native failures and removes staging ($failure)" {
        $fixture = New-Fixture
        $site = Join-Path $fixture 'web/dist'
        New-Item -ItemType Directory -Path (Join-Path $site '_framework'), (Join-Path $site 'assets'), (Join-Path $site '.vite') -Force | Out-Null
        $entry = if ($failure -eq 'base') { '/PS5SaveGame-Toolkit/app/assets/app.js' } else { '/assets/app.js' }
        Set-Content -LiteralPath (Join-Path $site 'index.html') "<div id=`"root`"></div><script src=`"$entry`"></script>"
        Set-Content -LiteralPath (Join-Path $site 'assets/app.js') 'app'
        Set-Content -LiteralPath (Join-Path $site '_framework/dotnet.js') 'runtime'
        Set-Content -LiteralPath (Join-Path $site '.vite/manifest.json') '{}'
        New-Item -ItemType Directory -Path (Join-Path $fixture 'docs/licenses') -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $fixture 'LICENSE') 'test license'
        Set-Content -LiteralPath (Join-Path $fixture 'THIRD-PARTY-NOTICES') 'test notices'
        $output = Join-Path $fixture 'packages [literal] space'
        New-Item -ItemType Directory -Path $output | Out-Null
        Set-Content -LiteralPath (Join-Path $output 'keep.txt') 'caller data'
        $bases = [Collections.Generic.List[object]]::new()
        $savedBase = $env:SAVETOOLKIT_WEB_BASE
        $env:SAVETOOLKIT_WEB_BASE = '/caller/'
        function npm {
            $bases.Add($env:SAVETOOLKIT_WEB_BASE)
            $global:LASTEXITCODE = if ($failure -eq 'npm') { 17 } else { 0 }
        }
        function dotnet {
            $global:LASTEXITCODE = if ($args[0] -eq $failure) { 17 } else { 0 }
            if ($args[0] -eq 'msbuild') { return '0.1.0-alpha.0.7' }
        }
        $errorText = ''
        try { & (Join-Path $fixture 'scripts/package.ps1') -OutputDirectory $output }
        catch { $errorText = "$_`n$($_.ScriptStackTrace)" }
        finally {
            $callerBase = $env:SAVETOOLKIT_WEB_BASE
            $env:SAVETOOLKIT_WEB_BASE = $savedBase
        }
        Assert-True ($callerBase -eq '/caller/') 'The caller base path must be restored.'
        Assert-True (@($bases | Where-Object { $_ -ne '/' }).Count -eq 0) 'The web build must use the root base path.'
        if ($failure -eq 'none') {
            Assert-True (!$errorText) "Unexpected failure: $errorText"
            $archive = Join-Path $output 'SaveToolkit-0.1.0-alpha.0.7-web.zip'
            Assert-True (Test-Path -LiteralPath $archive) 'Package must be created in a literal path with spaces/brackets.'
            $hash = (Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()
            Assert-True ((Get-Content -LiteralPath "$archive.sha256" -Raw).StartsWith("$hash  ")) 'Checksum must describe the produced archive.'
            $zip = [IO.Compression.ZipFile]::OpenRead($archive)
            try {
                $names = @($zip.Entries.FullName | ForEach-Object { $_.Replace('\', '/') })
                foreach ($name in @('index.html', 'assets/app.js', '_framework/dotnet.js', 'README.txt', 'LICENSE')) {
                    Assert-True ($names -contains "SaveToolkit-0.1.0-alpha.0.7-web/$name") "Archive must retain its top-level version folder and $name."
                }
                Assert-True (!($names -match '/\.vite/')) 'The Vite manifest is not part of the packaged site.'
            }
            finally { $zip.Dispose() }
            # Repeated invocation must replace the same named archive without damaging caller data.
            & (Join-Path $fixture 'scripts/package.ps1') -OutputDirectory $output -SkipWebBuild
        }
        elseif ($failure -eq 'base') { Assert-True ($errorText -match 'root base path') "A sub-path build must be refused: $errorText" }
        else { Assert-True ($errorText -match 'failed|Could not resolve') "Native failure must propagate: $errorText" }
        Assert-True (@(Get-ChildItem -LiteralPath $output -Directory).Count -eq 0) 'Staging must be removed after success/failure.'
        Assert-True ((Get-Content -LiteralPath (Join-Path $output 'keep.txt')) -eq 'caller data') 'Caller output must remain intact.'
    }
}
