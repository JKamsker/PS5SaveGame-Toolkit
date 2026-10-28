using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.BaldursGate3;

public sealed class BaldursGate3Plugin : IGamePlugin
{
    public PluginInfo Info { get; } = new("bg3", "Baldur's Gate 3", "0.6.0")
    {
        Description = "Experimental NewAge HP/ability/stack storage and metadata editing, planned inventory reorder/quantity restoration, spell graphs, attributed catalogs and verified v18 checksums. Console validation pending.",
        Games = [new GameTitle("PPSA18463", "Baldur's Gate 3")],
        Maturity = PluginMaturity.Experimental,
        References =
        [
            "https://github.com/Norbyte/lslib",
            "https://github.com/Norbyte/lslib/blob/master/LSLib/LS/PackageFormat.cs",
            "https://github.com/Norbyte/lslib/blob/master/LSLib/LS/Resources/LSF/LSFReader.cs",
            "https://github.com/DanFinchUK/bg3-savefile-parser/tree/9578ff7c46a1aa40c805f6b7beecf907f10fb3b8",
            "https://bg3.wiki/w/index.php?title=Gold&oldid=419021",
            "https://bg3.wiki/w/index.php?title=Potion_of_Healing&oldid=421944",
            "https://bg3.wiki/w/index.php?title=Longsword&oldid=420599",
            "https://bg3.wiki/wiki/Shortbow",
            "https://bg3.wiki/wiki/Lae%27zel",
            "https://bg3.wiki/wiki/Shadowheart",
            "https://bg3.wiki/wiki/The_Dark_Urge_(Origin)",
            "https://bg3.wiki/w/index.php?title=Equipment&oldid=405329",
            "https://bg3.wiki/w/index.php?title=Spells&oldid=433193",
            "https://bg3.wiki/w/index.php?title=Passive_feature&oldid=421706",
            "https://bg3.wiki/w/index.php?title=Companions&oldid=439368",
            "https://bg3.wiki/w/index.php?title=Camp_Supplies&oldid=409610",
            "https://bg3.wiki/w/index.php?title=Supply_Pack&oldid=423731",
            "https://bg3.wiki/w/index.php?title=Camp_Supply_Sack&oldid=416884",
            "https://bg3.wiki/w/index.php?title=Guidance&oldid=413520",
            "https://bg3.wiki/w/index.php?title=Fire_Bolt&oldid=373237",
            "https://bg3.wiki/wiki/bg3wiki:Copyrights",
            "https://github.com/Hawk-on/bg3-save-editor/blob/master/FIELD_MAPPING.md",
            "https://bg3.wiki/wiki/Experience",
            "https://github.com/Wahkah/BG3SE",
            "https://github.com/Serofix-code/bg3-save-editor",
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

    public ISaveDocument Open(ISaveFile file) => Open(file, IReferenceContext.Empty);

    public ISaveDocument Open(ISaveFile file, IReferenceContext references)
    {
        try
        {
            BinaryData.Size((ulong)file.Length);
            return BaldursGate3Document.Open(file, references);
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
