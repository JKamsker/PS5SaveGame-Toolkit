using System.Text.Json;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.XorJson;

internal static class JsonCollections
{
    private const int LargeArrayThreshold = 100;
    public static string Id(JsonValueToken array) => $"json/array/{array.Index}";

    public static IReadOnlyList<CharacterCollection> Build(IReadOnlyList<JsonValueToken> nodes, IReferenceContext references, bool character)
    {
        var collections = character ? CharacterCollections.Build(nodes[0], references).ToList() : [];
        foreach (var array in nodes.Where(n => n.Kind == JsonTokenType.StartArray && n.Children.Count >= LargeArrayThreshold))
            collections.Add(new CharacterCollection(Id(array), "JSON array: " + array.Label, "Raw",
                [new QueryableColumn("id", "Stored index"), new QueryableColumn("value", "Value"), new QueryableColumn("details", "Raw details")],
                array.Children, row => new CharacterRow(row, new Dictionary<string, CharacterCell>(StringComparer.Ordinal)
                {
                    ["id"] = new(row.Label),
                    ["value"] = row.IsContainer ? new(row.Kind == JsonTokenType.StartArray ? "Array" : "Object") : new(row.Scalar.ToString(), row),
                })));
        return collections;
    }
}
