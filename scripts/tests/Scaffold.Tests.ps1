Invoke-Case 'scaffold escapes C# literals and refuses to overwrite' {
    $fixture = New-Fixture
    $script = Join-Path $fixture 'scripts/new-plugin.ps1'
    $game = "Quoted `"Game`" \ path`r`nnext"
    & $script -Name ExampleGame -Id example-game -TitleIds 'PPSA00000' -GameName $game
    $source = Get-Content -LiteralPath (Join-Path $fixture 'plugins/ExampleGame/SaveToolkit.Plugins.ExampleGame/ExampleGamePlugin.cs') -Raw
    Assert-True ($source.Contains('"Quoted \"Game\" \\ path\r\nnext"')) 'Game name must be a valid escaped C# literal.'
    $files = @(Get-ChildItem -LiteralPath (Join-Path $fixture 'plugins') -Recurse -File)
    $before = @($files | Get-FileHash | ForEach-Object Hash) -join ','
    $rejected = $false
    try { & $script -Name ExampleGame -Id replacement } catch { $rejected = $true }
    Assert-True $rejected 'Existing plugins must be rejected.'
    $after = @($files | Get-FileHash | ForEach-Object Hash) -join ','
    Assert-True ($before -eq $after) 'Rejected scaffolding must not modify files.'
}
