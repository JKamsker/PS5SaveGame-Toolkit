using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.GenericJson;

public sealed class GenericJsonPlugin : IGamePlugin, ISaveDecoderProvider
{
    private const int ProbeHeaderBytes = 64;
    public IReadOnlyList<ISaveDecoderHook> Decoders { get; } = [new JsonTitleDecoder()];

    public PluginInfo Info { get; } = new("format.json", "Generic JSON", "0.2.0")
    {
        Description = "UTF-8 JSON token editor with warning-labelled, previewed title adapters; preserves untouched bytes and formatting.",
        Games = [],
        Maturity = PluginMaturity.Experimental,
        References = ["https://www.rfc-editor.org/rfc/rfc8259", "https://learn.microsoft.com/dotnet/api/system.text.json.utf8jsonreader",
            "https://prosperopatches.com/PPSA02343", "https://github.com/andshrew/PlayStation-PS1HD",
            "https://json-schema.org/understanding-json-schema/", "https://johnssaveeditors.com/games/it-takes-two/",
            JsonProfileMap.GuideUrl,
            "https://www.cyberpunk.net/en/news/41435/patch-1-5-next-generation-update-list-of-changes",
            "https://github.com/JKamsker/PS5SaveGame-Toolkit/blob/main/docs/plans/GenericJson.md",
            "https://github.com/steliosc/Cyberpunk-2077-Custom-Controls-Guide/blob/main/UserSettings.json"],
    };

    public ProbeResult Probe(ISaveFile file)
    {
        if (file.Length is <= 0 or > JsonTokenTree.MaxBytes)
        {
            return ProbeResult.None;
        }

        try
        {
            var prefix = file.ReadHeader(ProbeHeaderBytes).AsSpan();
            if (prefix.StartsWith("\uFEFF"u8))
            {
                prefix = prefix[3..];
            }

            var start = 0;
            while (start < prefix.Length && prefix[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            {
                start++;
            }

            if (start < prefix.Length && prefix[start] is not ((byte)'{' or (byte)'[' or (byte)'"' or (byte)'-'
                or (byte)'t' or (byte)'f' or (byte)'n' or >= (byte)'0' and <= (byte)'9'))
            {
                return ProbeResult.None;
            }

            JsonTokenValidation.Validate(file.ReadAllBytes());
            return ProbeResult.Match(ProbeScore.Format, "UTF-8 JSON");
        }
        catch (SaveFormatException)
        {
            return ProbeResult.None;
        }
    }

    public ISaveDocument Open(ISaveFile file) => Open(file, IReferenceContext.Empty);

    public ISaveDocument Open(ISaveFile file, IReferenceContext references)
    {
        if (file.Length is <= 0 or > JsonTokenTree.MaxBytes)
        {
            throw new SaveFormatException("JSON files must be between 1 byte and 16 MiB.");
        }

        var bytes = file.ReadAllBytes();
        var tree = JsonTokenTree.Parse(bytes);
        return new JsonSaveDocument(file, tree, references);
    }
}
