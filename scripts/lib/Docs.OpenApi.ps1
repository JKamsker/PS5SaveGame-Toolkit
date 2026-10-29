# Export from the current application without opening a fixed port or accessing user data.
function Export-DocsOpenApi {
    param([string]$Assembly, [string]$ScratchDirectory, [string]$Destination)

    $hostRoot = Join-Path $ScratchDirectory 'schema-host'
    New-Item -ItemType Directory -Force -Path $hostRoot | Out-Null
    $stdout = Join-Path $hostRoot 'stdout.log'
    $stderr = Join-Path $hostRoot 'stderr.log'
    $arguments = @(
        ('"{0}"' -f $Assembly), '--urls', 'http://127.0.0.1:0',
        '--environment', 'Production',
        ('"--Toolkit:DataRoot={0}"' -f (Join-Path $hostRoot 'workspaces')),
        ('"--Toolkit:PackRoot={0}"' -f (Join-Path $hostRoot 'packs')),
        '--Logging:LogLevel:Default=Information',
        '--Logging:LogLevel:Microsoft.Hosting.Lifetime=Information'
    )
    $start = @{
        FilePath = (Get-Command dotnet).Source
        ArgumentList = $arguments
        WorkingDirectory = $hostRoot
        RedirectStandardOutput = $stdout
        RedirectStandardError = $stderr
        PassThru = $true
    }
    if ($IsWindows) { $start.WindowStyle = 'Hidden' }
    $process = Start-Process @start
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(60)
        $origin = $null
        while ([DateTime]::UtcNow -lt $deadline) {
            if ($process.HasExited) { throw "Schema host exited. See $stdout and $stderr." }
            $log = Get-Content -LiteralPath $stdout -Raw -ErrorAction SilentlyContinue
            if ($log -match 'Now listening on: (http://127\.0\.0\.1:\d+)') {
                $origin = $Matches[1]
                break
            }
            Start-Sleep -Milliseconds 200
        }
        if (-not $origin) { throw "Schema host did not start within 60 seconds. See $stdout." }
        Invoke-WebRequest -Uri "$origin/openapi/v1.json" -OutFile $Destination -TimeoutSec 60 | Out-Null
    }
    finally {
        if (-not $process.HasExited) { $process.Kill($true) }
        $process.WaitForExit()
        $process.Dispose()
    }
}
