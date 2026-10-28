using System.Globalization;
using System.Security.Cryptography;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.EldenRing;

internal sealed class RegulationViews(byte[] bytes, IReferenceCatalog? layouts = null)
{
    public const string CollectionId = "regulation/tables";
    private readonly Lazy<Metadata> _metadata = new(() => Read(bytes));
    private static IReadOnlyList<QueryableColumn> Columns =>
        [new("name", "PARAM file"), new("rows", "Rows"), new("version", "Data / format version"), new("size", "Bytes"),
            new("schema", "Technical reference layout (not validated field decoding)")];

    // Advertising the collection must not trigger 37 MiB of decoding during initial view construction.
    // Its table is published only after lazy inspection knows the real count; query also resolves it.
    public IReadOnlyList<QueryableCollection> Collections => _metadata.IsValueCreated ? [Collection] : [];
    private QueryableCollection Collection => new(CollectionId, "Embedded regulation directory (inferred)", Columns, _metadata.Value.Tables.Count)
    {
        Description = "Row counts and bounds, with separately attributed technical layout references. No decoded field values or quantity limits.",
    };

    public IReadOnlyList<ViewNode> BuildView()
    {
        var metadata = _metadata.Value;
        if (metadata.Tables.Count == 0) return metadata.Notes;
        return [.. metadata.Notes, new QueryableTableNode(CollectionId, Collection.Title, Columns, metadata.Tables.Count)
            { Description = Collection.Description }];
    }

    public QueryableCollectionPage Query(QueryableCollectionQuery query)
    {
        var tables = _metadata.Value.Tables;
        var result = BrowseQuery.Apply(tables, query, Columns, table => table.Name, Text,
            (table, column) => column switch { "rows" => table.Rows, "size" => table.Size, _ => null });
        return new(CollectionId, Columns, result.Page.Select(table => new TableRow(table.Name,
            Columns.ToDictionary(column => column.Key, column => (ViewNode)new TextNode(Text(table, column.Key))))).ToArray(),
            tables.Count, result.Filtered, result.Offset, result.Limit);
    }

    private string Text(RegulationTable table, string column) => column switch
    {
        "name" => table.Name, "rows" => table.Rows.ToString(CultureInfo.InvariantCulture),
        "size" => table.Size.ToString(CultureInfo.InvariantCulture), "version" => $"{table.DataVersion} / {table.FormatVersion}",
        "schema" => Schema(table),
        _ => throw new ArgumentException("Unknown regulation column."),
    };

    private string Schema(RegulationTable table)
    {
        const string extension = ".param";
        var key = table.Name.EndsWith(extension, StringComparison.Ordinal) ? table.Name[..^extension.Length] : table.Name;
        var reference = layouts?.Find(key + ":schema");
        return reference is null ? "No compatible technical reference." : "Reference only: "
            + reference.Attributes.GetValueOrDefault("fieldCount", "Unknown") + " fields; source data version "
            + reference.Attributes.GetValueOrDefault("dataVersion", "Unknown") + $", save {table.DataVersion}. "
            + "Version equality does not prove PS5 compatibility. " + ReferenceBrowser.Details(reference);
    }

    private static Metadata Read(byte[] bytes)
    {
        try
        {
            var allocation = bytes.AsSpan(EldenRingLayout.RegulationOffset, EldenRingLayout.RegulationSize);
            var archive = RegulationArchiveReader.Read(RegulationDecoder.Decode(allocation));
            return new(archive.Tables,
            [
                new TextNode("Reference-pack compatibility key (SHA-256 of the preserved regulation allocation): "
                    + Convert.ToHexStringLower(SHA256.HashData(allocation))) { Monospace = true },
                new TextNode($"Read-only DCX/DFLT → BND4 {archive.BinderVersion}; {archive.DecodedSize.ToString(CultureInfo.InvariantCulture)} decoded bytes. "
                    + "Binder version is not an application patch. PARAM row directories are bounded; field schemas and legal item caps remain unverified."),
                new TextNode("Format source: ClayAmore/ER-Save-Editor 014107f0 (MIT; retrieved 2026-10-28), "
                    + "https://github.com/ClayAmore/ER-Save-Editor/tree/014107f0ca1cff867b5f7565cb79d6684f9b35df/src/util. "
                    + "Names below identify parameter files embedded in this save; no game descriptions or artwork are exported."),
            ]);
        }
        catch (Exception exception) when (exception is SaveFormatException or CryptographicException)
        {
            return new([], [new TextNode("Regulation metadata unavailable: " + exception.Message + " Original encrypted bytes are preserved.") { Tone = Tone.Warning }]);
        }
    }

    private sealed record Metadata(IReadOnlyList<RegulationTable> Tables, IReadOnlyList<ViewNode> Notes);
}
