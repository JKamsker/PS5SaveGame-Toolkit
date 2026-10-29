<#
.SYNOPSIS
  Scaffolds a plugin project plus its conformance test project.
.EXAMPLE
  ./scripts/new-plugin.ps1 -Name Pragmata -Id pragmata -TitleIds PPSA02530 -GameName "Pragmata"
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Z][A-Za-z0-9]+$')][string]$Name,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9]+([.-][a-z0-9]+)*$')][string]$Id,
    [string[]]$TitleIds = @(),
    [string]$GameName = $Name
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

$src = New-Item -ItemType Directory -Force (Join-Path $pluginDir $project)
$tests = New-Item -ItemType Directory -Force (Join-Path $pluginDir "$project.Tests")

$gameLiteral = ConvertTo-CSharpLiteral $GameName
$games = ($TitleIds | ForEach-Object { "new GameTitle($(ConvertTo-CSharpLiteral $_), $gameLiteral)" }) -join ', '
$titleList = ($TitleIds | ForEach-Object { ConvertTo-CSharpLiteral $_ }) -join ', '

Set-Content -LiteralPath (Join-Path $src "$project.csproj") @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>$project</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <!-- Plugins depend on the SDK only (never on Core/Api). -->
    <ProjectReference Include="..\..\..\src\SaveToolkit.Sdk\SaveToolkit.Sdk.csproj" />
  </ItemGroup>
</Project>
"@

Set-Content -LiteralPath (Join-Path $src "${Name}Plugin.cs") @"
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace $project;

public sealed class ${Name}Plugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("$Id", $gameLiteral, "0.1.0")
    {
        Description = "TODO",
        Games = [$games],
        Maturity = PluginMaturity.ReadOnly,
    };

    public ProbeResult Probe(ISaveFile file) => ProbeResult.None;

    public ISaveDocument Open(ISaveFile file) => throw new SaveFormatException("Not implemented yet.");
}
"@

Set-Content -LiteralPath (Join-Path $tests "$project.Tests.csproj") @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\$project\$project.csproj" />
    <ProjectReference Include="..\..\..\src\SaveToolkit.Testing\SaveToolkit.Testing.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
"@

Set-Content -LiteralPath (Join-Path $tests "${Name}ConformanceTests.cs") @"
using SaveToolkit.Testing;

namespace $project.Tests;

public sealed class ${Name}ConformanceTests : PluginConformanceTests<${Name}Plugin>
{
    protected override IReadOnlyList<string> SampleTitleIds => [$titleList];
}
"@

Set-Content -LiteralPath (Join-Path $pluginDir 'README.md') @"
# $GameName plugin (``$Id``)

Titles: $($TitleIds -join ', ')

## Format

TODO: document the file layout, encryption/compression, checksums and what is verified vs. inferred.

## Sources

TODO: links to upstream tools, wikis and research used.
"@

Write-Host "Created plugins/$Name. Build: dotnet test --project plugins/$Name/$project.Tests"
