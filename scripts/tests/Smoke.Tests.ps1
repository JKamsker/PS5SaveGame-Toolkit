foreach ($failure in @('none', 'ui', 'version', 'start', 'checksum')) {
    Invoke-Case "smoke isolates logs, quotes arguments and cleans extraction ($failure)" {
        $fixture = New-Fixture
        $packages = Join-Path $fixture 'packages'
        $scratch = Join-Path $fixture 'smoke space'
        $bundle = Join-Path $fixture 'bundle space'
        New-Item -ItemType Directory -Path $packages, $bundle, $scratch | Out-Null
        Set-Content -LiteralPath (Join-Path $bundle 'placeholder') 'test'
        $archive = Join-Path $packages 'SaveToolkit-1.2.3-win-x64.zip'
        Compress-Archive -LiteralPath $bundle -DestinationPath $archive
        $hash = (Get-FileHash -LiteralPath $archive).Hash
        if ($failure -eq 'checksum') { $hash = 'invalid' }
        Set-Content -LiteralPath "$archive.sha256" "$hash  $([IO.Path]::GetFileName($archive))"
        $starts = [Collections.Generic.List[object]]::new()
        $stops = [Collections.Generic.List[int]]::new()
        function Start-Process {
            param($FilePath, $ArgumentList, $WorkingDirectory, [switch]$PassThru,
                $RedirectStandardOutput, $RedirectStandardError, $WindowStyle)
            if ($failure -eq 'start') { throw 'Simulated start failure.' }
            $starts.Add(@{ Output = $RedirectStandardOutput; Error = $RedirectStandardError; Arguments = $ArgumentList })
            Set-Content -LiteralPath $RedirectStandardOutput 'stdout'
            Set-Content -LiteralPath $RedirectStandardError 'stderr'
            $process = [pscustomobject]@{ Id = 123; HasExited = $false }
            $process | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value { $this.HasExited = $true }
            return $process
        }
        function Stop-Process { param($Id, [switch]$Force) $stops.Add($Id) }
        function Invoke-RestMethod { param($Uri, $TimeoutSec) return @{ version = '1.2.3+commit' } }
        function Invoke-WebRequest {
            param($Uri, $TimeoutSec)
            if ($failure -eq 'ui') { throw 'Simulated UI failure.' }
            return @{ StatusCode = 200; Content = '<div id="root"><script src="/assets/app.js"></script>'; Headers = @{ 'Content-Type' = 'text/javascript' } }
        }
        $script = Join-Path $fixture 'scripts/smoke-package.ps1'
        $expected = if ($failure -eq 'version') { '9.9.9' } else { '1.2.3' }
        $errorText = ''
        try { & $script -PackageDirectory $packages -Runtime win-x64 -ScratchDirectory $scratch -ExpectedVersion $expected }
        catch { $errorText = "$_" }
        if ($failure -eq 'none') {
            Assert-True (!$errorText) "Unexpected failure: $errorText"
            & $script -PackageDirectory $packages -Runtime win-x64 -ScratchDirectory $scratch -ExpectedVersion $expected
            Assert-True ($starts[0].Output -ne $starts[1].Output -and $starts[0].Error -ne $starts[1].Error) 'Smoke invocations must not share log files.'
            Assert-True ($starts[0].Arguments[3] -match '^".*bundle space"$') 'Content root containing spaces must be quoted.'
        }
        else { Assert-True ([bool]$errorText) 'Failure must propagate.' }
        Assert-True ($stops.Count -eq $starts.Count) 'Every started process must be stopped.'
        Assert-True (@(Get-ChildItem -LiteralPath $scratch -Directory).Count -eq 0) 'Extractions must be removed after success/failure.'
        foreach ($start in $starts) { Assert-True (Test-Path -LiteralPath $start.Error) 'Per-run diagnostic logs must remain.' }
    }
}
