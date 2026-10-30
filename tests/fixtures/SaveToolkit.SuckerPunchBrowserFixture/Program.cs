using System.Text.Json;
using SaveToolkit.Plugins.SuckerPunch.Tests;

if (args.Length != 1 || !Path.IsPathFullyQualified(args[0]))
    throw new ArgumentException("Supply one absolute, process-owned scratch output directory.");

var directory = args[0];
Directory.CreateDirectory(directory);
var fixture = SuckerPunchCaptionAcceptanceFixtures.Create();
File.WriteAllBytes(Path.Combine(directory, "gameplay.bin"), fixture.GameplayBytes.ToArray());
File.WriteAllBytes(Path.Combine(directory, "param.sfo"), fixture.SfoBytes.ToArray());
File.WriteAllBytes(Path.Combine(directory, "companion.bin"), fixture.CompanionBytes.ToArray());
var manifest = new
{
    fixture.TitleId,
    fixture.PrimaryRelativePath,
    fixture.SfoRelativePath,
    fixture.CompanionRelativePath,
    fixture.Caption,
    fixture.MaxUtf8Bytes,
};
await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest,
    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
