foreach ($failure in @('none', 'asset', 'runtime', 'version', 'checksum')) {
    Invoke-Case "smoke verifies the static archive and cleans extraction ($failure)" {
        $fixture = New-Fixture
        $packages = Join-Path $fixture 'packages'
        $scratch = Join-Path $fixture 'smoke space'
        $bundle = Join-Path $fixture 'SaveToolkit-1.2.3-web'
        New-Item -ItemType Directory -Path $packages, $scratch, (Join-Path $bundle 'assets'), (Join-Path $bundle '_framework'), (Join-Path $bundle 'licenses') | Out-Null
        foreach ($file in @('README.txt', 'LICENSE', 'THIRD-PARTY-NOTICES', '_framework/dotnet.js', 'assets/app.css')) {
            Set-Content -LiteralPath (Join-Path $bundle $file) 'test'
        }
        if ($failure -ne 'asset') { Set-Content -LiteralPath (Join-Path $bundle 'assets/app.js') 'app' }
        Set-Content -LiteralPath (Join-Path $bundle 'index.html') '<link href="/assets/app.css" rel="stylesheet"><div id="root"><script src="/assets/app.js"></script>'
        if ($failure -ne 'runtime') {
            # A length-prefixed informational version string, as stored in the webcil assembly.
            $version = [Text.Encoding]::ASCII.GetBytes('1.2.3+commit')
            [IO.File]::WriteAllBytes((Join-Path $bundle '_framework/SaveToolkit.Browser.abc123.wasm'),
                [byte[]](@(0, 0x39, 7) + @($version.Length) + $version + @(0, 0, 1)))
        }
        $archive = Join-Path $packages 'SaveToolkit-1.2.3-web.zip'
        Compress-Archive -LiteralPath $bundle -DestinationPath $archive
        $hash = (Get-FileHash -LiteralPath $archive).Hash
        if ($failure -eq 'checksum') { $hash = 'invalid' }
        Set-Content -LiteralPath "$archive.sha256" "$hash  $([IO.Path]::GetFileName($archive))"
        $script = Join-Path $fixture 'scripts/smoke-package.ps1'
        $expected = if ($failure -eq 'version') { '1.2' } else { '1.2.3' }
        $errorText = ''
        try { & $script -PackageDirectory $packages -ScratchDirectory $scratch -ExpectedVersion $expected }
        catch { $errorText = "$_" }
        if ($failure -eq 'none') {
            Assert-True (!$errorText) "Unexpected failure: $errorText"
            & $script -PackageDirectory $packages -ScratchDirectory $scratch
        }
        else { Assert-True ([bool]$errorText) 'Failure must propagate.' }
        Assert-True (@(Get-ChildItem -LiteralPath $scratch -Directory).Count -eq 0) 'Extractions must be removed after success/failure.'
    }
}
