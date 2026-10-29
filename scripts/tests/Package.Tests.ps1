foreach ($failure in @('none', 'restore', 'msbuild', 'publish', 'npm', 'tar')) {
    Invoke-Case "packaging checks native failures and removes staging ($failure)" {
        $fixture = New-Fixture
        $web = Join-Path $fixture 'src/SaveToolkit.Api/wwwroot'
        New-Item -ItemType Directory -Path $web -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $web 'index.html') '<div id="root"></div>'
        New-Item -ItemType Directory -Path (Join-Path $fixture 'web'), (Join-Path $fixture 'docs/licenses') -Force | Out-Null
        Set-Content -LiteralPath (Join-Path $fixture 'LICENSE') 'test license'
        Set-Content -LiteralPath (Join-Path $fixture 'THIRD-PARTY-NOTICES') 'test notices'
        $output = Join-Path $fixture 'packages [literal] space'
        New-Item -ItemType Directory -Path $output | Out-Null
        Set-Content -LiteralPath (Join-Path $output 'keep.txt') 'caller data'
        function npm { $global:LASTEXITCODE = if ($failure -eq 'npm') { 17 } else { 0 } }
        function dotnet {
            $global:LASTEXITCODE = if ($args[0] -eq $failure) { 17 } else { 0 }
            if ($args[0] -eq 'msbuild') { return '0.1.0-alpha.0.7' }
            if ($args[0] -eq 'publish') {
                $bundle = $args[[Array]::IndexOf($args, '--output') + 1]
                New-Item -ItemType Directory -Path (Join-Path $bundle 'wwwroot') -Force | Out-Null
                foreach ($file in @('wwwroot/index.html', 'e_sqlite3.dll', 'libe_sqlite3.so', 'SaveToolkit.Api.deps.json', 'SaveToolkit.Api')) {
                    Set-Content -LiteralPath (Join-Path $bundle $file) 'test content'
                }
            }
        }
        function tar { $global:LASTEXITCODE = 17 }
        function chmod { $global:LASTEXITCODE = 0 }
        $errorText = ''
        $rid = if ($failure -eq 'tar') { 'linux-x64' } else { 'win-x64' }
        try { & (Join-Path $fixture 'scripts/package.ps1') -Runtime $rid -OutputDirectory $output }
        catch { $errorText = "$_`n$($_.ScriptStackTrace)" }
        if ($failure -eq 'none') {
            Assert-True (!$errorText) "Unexpected failure: $errorText"
            $archive = Join-Path $output 'SaveToolkit-0.1.0-alpha.0.7-win-x64.zip'
            Assert-True (Test-Path -LiteralPath $archive) 'Package must be created in a literal path with spaces/brackets.'
            $hash = (Get-FileHash -LiteralPath $archive).Hash.ToLowerInvariant()
            Assert-True ((Get-Content -LiteralPath "$archive.sha256" -Raw).StartsWith("$hash  ")) 'Checksum must describe the produced archive.'
            $zip = [IO.Compression.ZipFile]::OpenRead($archive)
            try {
                Assert-True ($zip.Entries.FullName -contains 'SaveToolkit-0.1.0-alpha.0.7-win-x64/start.cmd') 'Archive must retain its top-level version/RID folder and launcher.'
            }
            finally { $zip.Dispose() }
            # Repeated invocation must replace the same named archive without damaging caller data.
            & (Join-Path $fixture 'scripts/package.ps1') -Runtime $rid -OutputDirectory $output
        }
        else { Assert-True ($errorText -match 'failed|Could not resolve') "Native failure must propagate: $errorText" }
        Assert-True (@(Get-ChildItem -LiteralPath $output -Directory).Count -eq 0) 'Publish staging must be removed after success/failure.'
        Assert-True ((Get-Content -LiteralPath (Join-Path $output 'keep.txt')) -eq 'caller data') 'Caller output must remain intact.'
    }
}
