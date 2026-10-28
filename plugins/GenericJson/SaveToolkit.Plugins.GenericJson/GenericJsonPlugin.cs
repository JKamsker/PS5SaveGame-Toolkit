using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.GenericJson;

public sealed class GenericJsonPlugin : IGamePlugin
{
    private const int ProbeHeaderBytes = 64;

    public PluginInfo Info { get; } = new("format.json", "Generic JSON", "0.1.0")
    {
        Description = "UTF-8 JSON token editor; preserves untouched bytes and formatting.",
        Games = [],
        Maturity = PluginMaturity.Experimental,
        References = ["https://www.rfc-editor.org/rfc/rfc8259", "https://learn.microsoft.com/dotnet/api/system.text.json.utf8jsonreader"],
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

            _ = JsonTokenTree.Parse(file.ReadAllBytes());
            return ProbeResult.Match(ProbeScore.Format, "UTF-8 JSON");
        }
        catch (SaveFormatException)
        {
            return ProbeResult.None;
        }
    }

    public ISaveDocument Open(ISaveFile file)
    {
        if (file.Length is <= 0 or > JsonTokenTree.MaxBytes)
        {
            throw new SaveFormatException("JSON files must be between 1 byte and 16 MiB.");
        }

        var bytes = file.ReadAllBytes();
        return new JsonSaveDocument(file.FileName, file.RelativePath, bytes, JsonTokenTree.Parse(bytes));
    }
}
