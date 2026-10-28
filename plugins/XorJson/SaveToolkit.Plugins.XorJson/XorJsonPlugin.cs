using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.XorJson;

public sealed class XorJsonPlugin : IGamePlugin
{
    private const int ProbeHeaderBytes = 64;
    public PluginInfo Info { get; } = new("format.json-xor", "Obfuscated JSON", "0.1.0")
    {
        Description = "Lossless JSON token editing for XOR-1 and byte-minus-one encoding.",
        Games = [new GameTitle("PPSA16064", "Minecraft Dungeons II")],
        Maturity = PluginMaturity.Experimental,
        References = ["https://prosperopatches.com/PPSA16064", "https://www.rfc-editor.org/rfc/rfc8259"],
    };

    public ProbeResult Probe(ISaveFile file)
    {
        if (file.Length is <= 0 or > JsonTokenTree.MaxBytes) return ProbeResult.None;
        try
        {
            if (!JsonEncoding.HasEncodedPrefix(file.ReadHeader(ProbeHeaderBytes))) return ProbeResult.None;
            var decoded = JsonEncoding.Decode(file.ReadAllBytes());
            return ProbeResult.Match(ProbeScore.Format, decoded.Encoding.Label);
        }
        catch (Exception ex) when (ex is SaveFormatException or IOException or ArgumentException)
        {
            return ProbeResult.None;
        }
    }

    public ISaveDocument Open(ISaveFile file)
    {
        if (file.Length is <= 0 or > JsonTokenTree.MaxBytes)
            throw new SaveFormatException("Obfuscated JSON must be between 1 byte and 16 MiB.");
        var decoded = JsonEncoding.Decode(file.ReadAllBytes());
        return new JsonSaveDocument(file.FileName, file.RelativePath, decoded.Bytes, decoded.Tree, decoded.Encoding);
    }
}
