using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.XorJson;

public sealed class XorJsonPlugin : IGamePlugin
{
    private const int ProbeHeaderBytes = 64;
    public PluginInfo Info { get; } = new("format.json-xor", "Obfuscated JSON / Dungeons II", "0.3.0")
    {
        Description = "Lossless exact JSON editing and read-only FCharacterSaveV1 inspection with attributed catalogs.",
        Games = [new GameTitle("PPSA16064", "Minecraft Dungeons II")],
        Maturity = PluginMaturity.Experimental,
        References = ["https://prosperopatches.com/PPSA16064", "https://www.rfc-editor.org/rfc/rfc8259",
            "https://github.com/IshiakiZ/mcd2-save-editor/tree/84ffee67b73e49d5b888e31257ce9767aa8ab826",
            "https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/LICENSE",
            "https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/hero.py",
            "https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/story.py",
            "https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/world.py",
            "https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/data/items.json",
            "https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/data/effects.json",
            "https://github.com/IshiakiZ/mcd2-save-editor/blob/84ffee67b73e49d5b888e31257ce9767aa8ab826/dungeons2_editor/data/world.json"],
    };

    public ProbeResult Probe(ISaveFile file)
    {
        if (file.Length is <= 0 or > JsonTokenTree.MaxBytes) return ProbeResult.None;
        try
        {
            var header = file.ReadHeader(ProbeHeaderBytes);
            if (!JsonEncoding.HasEncodedPrefix(header) && !header.Contains((byte)'{')) return ProbeResult.None;
            var decoded = JsonEncoding.Decode(file.ReadAllBytes());
            return ProbeResult.Match(decoded.Encoding.Plain ? ProbeScore.Engine : ProbeScore.Format, decoded.Encoding.Label);
        }
        catch (Exception ex) when (ex is SaveFormatException or IOException or ArgumentException or InvalidOperationException)
        {
            return ProbeResult.None;
        }
    }

    public ISaveDocument Open(ISaveFile file) => Open(file, IReferenceContext.Empty);

    public ISaveDocument Open(ISaveFile file, IReferenceContext references)
    {
        if (file.Length is <= 0 or > JsonTokenTree.MaxBytes)
            throw new SaveFormatException("Obfuscated JSON must be between 1 byte and 16 MiB.");
        var decoded = JsonEncoding.Decode(file.ReadAllBytes());
        return new JsonSaveDocument(file.FileName, file.RelativePath, decoded.Tree, decoded.Encoding, references);
    }
}
