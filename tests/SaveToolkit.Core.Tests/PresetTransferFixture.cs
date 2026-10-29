using System.Text.Json;
using SaveToolkit.Core.Presets;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests;

internal static class PresetTransferFixture
{
    internal static PresetStore Store(string? root = null) => new(new() { StorageRoot = root ?? TestZips.TempDir() });
    internal static ToolkitPreset Preset(string id = "portable", string name = "装備 · café 🧰") => new("1", id, name,
        new("test.planning", "1.0.0", ["PPSA99999"], null, null, null, true, "toolkit.action-parameters"),
        [new("refill", Values(), [new("owned-item", "semantic/potion", "Potion")])],
        DateTimeOffset.Parse("2026-10-29T10:00:00Z"), DateTimeOffset.Parse("2026-10-29T10:01:00Z"));

    internal static Dictionary<string, JsonElement> Values()
    {
        using var json = JsonDocument.Parse("""
            {"huge":900719925474099312345678901,"lexeme":1.2300e+120,"integer":"340282366920938463463374607431768211455",
             "exact":{"kind":"exactNumber","text":"-0.0012300e+99"},"union":{"kind":"union","type":"text","value":"日本語 🧰"},
             "boolean":true,"nothing":null}
            """);
        return json.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    internal static PresetImportPreview Resolve(PresetTransferService service, PresetImportPreview preview,
        params PresetImportChoice[] choices) => service.Resolve(new(preview.Token,
            choices.Length == 0 ? preview.Items.Select(item => new PresetImportChoice(item.Index, "keepBoth")).ToArray() : choices));
}
