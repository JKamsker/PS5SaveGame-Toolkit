using System.Text.Json;
using System.Security.Cryptography;
using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Diff;

namespace SaveToolkit.Plugins.UnrealGvas.Hogwarts;

/// <summary>F1 hook: align verified unique PS5 row tuples, hash full rows, never publish private cell values.</summary>
public sealed class HogwartsSaveDecoder : ISaveDecoderHook
{
    public string Id => "unrealgvas.hogwarts.rows";

    private const int EvidencePathDepth = 5;
    private const int MaxRows = 100_000;

    public bool CanDecode(ISaveFile file)
    {
        try
        {
            return file.Directory.TitleId == "PPSA01603" &&
                file.Directory.Name.StartsWith("HL00", StringComparison.Ordinal) &&
                file.ReadHeader(4).AsSpan().SequenceEqual("GVAS"u8);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    public void Decode(ISaveFile file, ISaveDecodeSink sink)
    {
        if (file.Length > Math.Min(sink.MaxFileBytes, GvasFile.MaxFileLength))
            throw new SaveFormatException("Hogwarts diff input exceeds bounds.");
        if (sink.MaxDepth < EvidencePathDepth)
        {
            sink.MarkTruncated("Hogwarts row evidence exceeds the requested depth limit.");
            return;
        }
        var gvas = GvasFile.Parse(file.ReadAllBytes());
        int visitedRows = 0;
        foreach (var node in gvas.Properties.Where(node => node.Name is "RawDatabaseImage" or "RawExclusiveImage"))
        {
            using var database = new HogwartsDatabase(node.Name, HogwartsImage.ReadArray(gvas, node));
            foreach (var table in database.Tables)
            {
                if (!sink.Add(table.Id + "/schema", "sha256", table.SchemaHash) ||
                    !sink.Add(table.Id + "/count", "integer", table.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                    return;
                bool ambiguous = table.IdentityColumns.Count == 0;
                if (ambiguous && table.Group == "Database tables") continue;
                var multiplicities = new Dictionary<string, int>(StringComparer.Ordinal);
                if (ambiguous)
                {
                    sink.Warn(table.Id + ": no proven unique row tuple; comparing full-row multisets, not inferring row correspondence.");
                }
                for (int offset = 0; offset < table.Count; offset += QueryableCollectionLimits.MaxPageSize)
                {
                    var page = database.Query(table, new() { Offset = offset, Limit = QueryableCollectionLimits.MaxPageSize, IncludeEmpty = true });
                    foreach (var row in page.Result.Rows)
                    {
                        if (visitedRows++ >= MaxRows)
                        {
                            sink.MarkTruncated("Hogwarts row evidence reached the 100,000-row traversal limit.");
                            return;
                        }
                        string hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(row.Skip(1).ToArray())));
                        if (ambiguous) multiplicities[hash] = multiplicities.GetValueOrDefault(hash) + 1;
                        else if (!sink.Add(table.Id + "/rows/" + table.RowKey(row), "row-sha256", hash)) return;
                    }
                }
                foreach (var (hash, count) in multiplicities.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    if (!sink.Add(table.Id + "/rows/content:" + hash, "row-multiplicity", count.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                        return;
            }
        }
    }
}
