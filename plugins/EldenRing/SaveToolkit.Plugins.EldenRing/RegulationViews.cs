using System.Globalization;
using System.Security.Cryptography;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing;

internal static class RegulationViews
{
    public static IReadOnlyList<ViewNode> Read(byte[] bytes)
    {
        try
        {
            var allocation = bytes.AsSpan(EldenRingLayout.RegulationOffset, EldenRingLayout.RegulationSize);
            var archive = RegulationArchiveReader.Read(RegulationDecoder.Decode(allocation));
            return
            [
                new TextNode("Reference-pack compatibility key (SHA-256 of the preserved regulation allocation): "
                    + Convert.ToHexStringLower(SHA256.HashData(allocation))) { Monospace = true },
                new TextNode($"Read-only DCX/DFLT → BND4 {archive.BinderVersion}; {archive.DecodedSize.ToString(CultureInfo.InvariantCulture)} decoded bytes. "
                    + "Binder version is not an application patch. PARAM row directories are bounded; field schemas and legal item caps remain unverified."),
                new TextNode("Format source: ClayAmore/ER-Save-Editor 014107f0 (MIT; retrieved 2026-10-28), "
                    + "https://github.com/ClayAmore/ER-Save-Editor/tree/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/util. "
                    + "Names below identify parameter files embedded in this save; no game descriptions or artwork are exported."),
                new TableNode([new("name", "PARAM file"), new("rows", "Rows"), new("version", "Data / format version"), new("size", "Bytes")],
                    archive.Tables.Select(table => new TableRow(table.Name, new Dictionary<string, ViewNode>
                    {
                        ["name"] = new TextNode(table.Name), ["rows"] = new TextNode(table.Rows.ToString(CultureInfo.InvariantCulture)),
                        ["version"] = new TextNode($"{table.DataVersion} / {table.FormatVersion}"),
                        ["size"] = new TextNode(table.Size.ToString(CultureInfo.InvariantCulture)),
                    })).ToArray()) { Title = "Embedded regulation directory (inferred)", Description = "Row counts and bounds only; no decoded field schema or quantity limits." },
            ];
        }
        catch (Exception exception) when (exception is SaveFormatException or CryptographicException)
        {
            return [new TextNode("Regulation metadata unavailable: " + exception.Message + " Original encrypted bytes are preserved.") { Tone = Tone.Warning }];
        }
    }
}
