<#
.SYNOPSIS
  Scaffolds a read-only synthetic example plus conformance and sample tests.
.EXAMPLE
  ./scripts/new-plugin.ps1 -Name ExampleGame -Id example-game -TitleIds PPSA00000 -GameName "Example Game"
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Z][A-Za-z0-9]+$', Options = 'CultureInvariant')][string]$Name,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9]+([.-][a-z0-9]+)*$', Options = 'CultureInvariant')][string]$Id,
    [ValidatePattern('^[A-Z]{4}[0-9]{5}$', Options = 'CultureInvariant')][string[]]$TitleIds = @(),
    [ValidateNotNullOrEmpty()][string]$GameName = $Name
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$pluginDir = Join-Path $root "plugins/$Name"
$project = "SaveToolkit.Plugins.$Name"
if (Test-Path -LiteralPath $pluginDir) { throw "plugins/$Name already exists." }

function ConvertTo-CSharpLiteral([string]$Value) {
    # JSON string escapes are also valid in ordinary C# string literals.
    return ConvertTo-Json -InputObject $Value -Compress
}

$gameLiteral = ConvertTo-CSharpLiteral $GameName
$games = ($TitleIds | ForEach-Object { "new GameTitle($(ConvertTo-CSharpLiteral $_), $gameLiteral)" }) -join ', '
# Generic plugins still need a sample title for the inherited conformance tests.
$sampleTitles = if ($TitleIds.Count) { $TitleIds } else { @('PPSA00000') }
$tokens = [ordered]@{
    '@PROJECT@' = $project
    '@NAME@' = $Name
    '@ID@' = $Id
    '@GAME_LITERAL@' = $gameLiteral
    '@GAMES@' = $games
    '@SAMPLE_TITLES@' = ($sampleTitles | ForEach-Object { ConvertTo-CSharpLiteral $_ }) -join ', '
    '@SAMPLE_TITLE@' = ConvertTo-CSharpLiteral @($sampleTitles)[0]
    '@GAME_NAME@' = $GameName
    '@TITLE_IDS@' = if ($TitleIds.Count) { $TitleIds -join ', ' } else { 'None (generic format plugin).' }
}
$templateRoot = Join-Path $PSScriptRoot 'templates/plugin'
$files = [ordered]@{
    'Plugin.cs.template' = "$project/${Name}Plugin.cs"
    'Document.cs.template' = "$project/${Name}Document.cs"
    'Plugin.csproj.template' = "$project/$project.csproj"
    'ConformanceTests.cs.template' = "$project.Tests/${Name}ConformanceTests.cs"
    'DocumentTests.cs.template' = "$project.Tests/${Name}DocumentTests.cs"
    'SampleTests.cs.template' = "$project.Tests/${Name}SampleTests.cs"
    'Tests.csproj.template' = "$project.Tests/$project.Tests.csproj"
    'README.md.template' = 'README.md'
}
foreach ($file in $files.GetEnumerator()) {
    $template = Get-Content -LiteralPath (Join-Path $templateRoot $file.Key) -Raw
    # Replace in one pass so user-supplied text containing a token stays literal.
    $content = [regex]::Replace($template, '@[A-Z_]+@', { param($match) $tokens[$match.Value] })
    $destination = Join-Path $pluginDir $file.Value
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Set-Content -LiteralPath $destination -Value $content -NoNewline -Encoding utf8
}

Write-Host "Created plugins/$Name (synthetic example only; replace before claiming game support)."
Write-Host "Build: dotnet build plugins/$Name/$project/$project.csproj"
Write-Host "Test:  dotnet test --project plugins/$Name/$project.Tests"
