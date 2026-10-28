using System.Text.Json;
using System.Security.Cryptography;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Plugins.UnrealGvas.Hogwarts;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

/// <summary>F1 hook: align verified unique PS5 row tuples, hash full rows, never publish private cell values.</summary>
public sealed class HogwartsSaveDecoder : ISaveDecoder
{
    public string Id => "unrealgvas.hogwarts.rows";

    public bool CanDecode(ISaveFile file) => file.Directory.TitleId == "PPSA01603" &&
        file.Directory.Name.StartsWith("HL00", StringComparison.Ordinal) && file.ReadHeader(4).AsSpan().SequenceEqual("GVAS"u8);

    public SemanticTree Decode(ISaveFile file, DecodeLimits limits)
    {
        if (file.Length > limits.MaxFileBytes) throw new SaveFormatException("Hogwarts diff input exceeds bounds.");
        var gvas = GvasFile.Parse(file.ReadAllBytes());
        var builder = new SemanticTreeBuilder(Id, limits);
        foreach (var node in gvas.Properties.Where(node => node.Name is "RawDatabaseImage" or "RawExclusiveImage"))
        {
            using var database = new HogwartsDatabase(node.Name, HogwartsImage.ReadArray(gvas, node));
            foreach (var table in database.Tables)
            {
                if (!builder.Add(table.Id + "/schema", "sha256", table.SchemaHash) ||
                    !builder.Add(table.Id + "/count", "integer", table.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                    return builder.Build();
                bool ambiguous = table.IdentityColumns.Count == 0;
                if (ambiguous && table.Group == "Database tables") continue;
                var multiplicities = new Dictionary<string, int>(StringComparer.Ordinal);
                if (ambiguous)
                {
                    builder.Warn(table.Id + ": no proven unique row tuple; comparing full-row multisets, not inferring row correspondence.");
                }
                for (int offset = 0; offset < table.Count; offset += QueryableCollectionLimits.MaxPageSize)
                {
                    var page = database.Query(table, new() { Offset = offset, Limit = QueryableCollectionLimits.MaxPageSize });
                    foreach (var row in page.Result.Rows)
                    {
                        string hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(row.Skip(1).ToArray())));
                        if (ambiguous) multiplicities[hash] = multiplicities.GetValueOrDefault(hash) + 1;
                        else if (!builder.Add(table.Id + "/rows/" + table.RowKey(row), "row-sha256", hash)) return builder.Build();
                    }
                }
                foreach (var (hash, count) in multiplicities.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    if (!builder.Add(table.Id + "/rows/content:" + hash, "row-multiplicity", count.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                        return builder.Build();
            }
        }
        return builder.Build();
    }
}
