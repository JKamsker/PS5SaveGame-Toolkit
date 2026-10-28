using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.BaldursGate3;

public sealed class BaldursGate3Plugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("bg3", "Baldur's Gate 3", "0.1.0")
    {
        Description = "Read-only LSPK package and LSOF/LSX resource explorer.",
        Games = [new GameTitle("PPSA18463", "Baldur's Gate 3")],
        Maturity = PluginMaturity.ReadOnly,
        References =
        [
            "https://github.com/Norbyte/lslib",
            "https://github.com/Norbyte/lslib/blob/master/LSLib/LS/PackageFormat.cs",
            "https://github.com/Norbyte/lslib/blob/master/LSLib/LS/Resources/LSF/LSFReader.cs",
        ],
    };

    public ProbeResult Probe(ISaveFile file)
    {
        if (!string.Equals(file.Directory.TitleId, "PPSA18463", StringComparison.OrdinalIgnoreCase))
        {
            return ProbeResult.None;
        }

        var header = file.ReadHeader(64).AsSpan();
        var extension = Path.GetExtension(file.FileName);
        var package = extension.Equals(".lsv", StringComparison.OrdinalIgnoreCase) && header.StartsWith("LSPK"u8);
        var binary = extension.Equals(".lsf", StringComparison.OrdinalIgnoreCase) && header.StartsWith("LSOF"u8);
        var xml = extension.Equals(".lsx", StringComparison.OrdinalIgnoreCase) && IsXmlHeader(header);
        return package || binary || xml ? ProbeResult.Match(ProbeScore.Title,
            package ? "BG3 save package" : "BG3 profile resource") : ProbeResult.None;
    }

    public ISaveDocument Open(ISaveFile file)
    {
        try
        {
            BinaryData.Size((ulong)file.Length);
            return BaldursGate3Document.Open(file);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or OverflowException or
            System.Xml.XmlException or ZstdSharp.ZstdException or InvalidOperationException)
        {
            throw new SaveFormatException($"Invalid BG3 save data: {exception.Message}", exception);
        }
    }

    private static bool IsXmlHeader(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith("\uFEFF"u8))
        {
            header = header[System.Text.Encoding.UTF8.Preamble.Length..];
        }

        if (header.StartsWith(System.Text.Encoding.Unicode.Preamble) || header.StartsWith(System.Text.Encoding.BigEndianUnicode.Preamble))
        {
            var encoding = header[0] == 0xff ? System.Text.Encoding.Unicode : System.Text.Encoding.BigEndianUnicode;
            var completeCharacters = header.Length - header.Length % sizeof(char);
            var text = encoding.GetString(header[sizeof(char)..completeCharacters]).TrimStart();
            return text.StartsWith("<?xml", StringComparison.Ordinal) || text.StartsWith("<save", StringComparison.Ordinal);
        }

        while (!header.IsEmpty && header[0] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            header = header[1..];
        }

        return header.StartsWith("<?xml"u8) || header.StartsWith("<save"u8);
    }
}
