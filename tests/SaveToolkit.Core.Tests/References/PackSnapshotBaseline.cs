using System.Security.Cryptography;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Core.Tests.References;

internal static class PackSnapshotBaseline
{
    internal const string FilePath = "tests/SaveToolkit.Core.Tests/References/EmbeddedPackHashes.txt";
    internal const string ResourceName = "SaveToolkit.Core.Tests.References.EmbeddedPackHashes.txt";
    internal const string PrintCommand = "dotnet test --project tests/SaveToolkit.Core.Tests --configuration Release " +
        "--filter-method '*PrintCurrentPackHashes' --explicit on --output Detailed";
    private const int Sha256HexLength = 64;

    internal static Dictionary<string, string> Read(TextReader reader)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Assert.True(fields.Length == 2 && fields[1].Length == Sha256HexLength && fields[1].All(Uri.IsHexDigit),
                $"{FilePath}:{lineNumber}: expected '<pack-id> <SHA-256>'.");
            Assert.True(result.TryAdd(fields[0], fields[1].ToLowerInvariant()),
                $"{FilePath}:{lineNumber}: duplicate pack id '{fields[0]}'.");
        }
        return result;
    }

    internal static IReadOnlyList<string> Differences(IReadOnlyDictionary<string, string> baseline,
        IReadOnlyDictionary<string, string> current)
    {
        var errors = new List<string>();
        foreach (var (id, hash) in current.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!baseline.TryGetValue(id, out var expected))
                errors.Add($"New embedded pack '{id}': add this line to {FilePath}:\n{id} {hash}\nPrint current values: {PrintCommand}");
            else if (expected != hash)
                errors.Add($"Pack '{id}' snapshot changed (pinned {expected}, current {hash}). " +
                    $"After reviewing the intentional change, edit the line beginning '{id} ' in {FilePath} to:\n{id} {hash}\n" +
                    $"Print current values: {PrintCommand}");
        }
        foreach (var id in baseline.Keys.Except(current.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            errors.Add($"Pinned pack '{id}' is no longer embedded. After reviewing its removal, delete the line beginning '{id} ' " +
                $"in {FilePath}. Print current values: {PrintCommand}");
        return errors;
    }

    internal static string Hash(LoadedReferencePack pack, JsonElement manifest)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        // Preserve the old decoded snapshot payload, resetting the hash for each pack.
        hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(pack.Info));
        var context = new ReferenceContext([pack], ["en"]);
        foreach (var dataset in manifest.GetProperty("datasets").EnumerateArray())
        {
            if (dataset.GetProperty("kind").GetString() == "records")
            {
                var catalog = pack.Catalog(dataset.GetProperty("id").GetString()!)!;
                foreach (var record in ReferenceCatalogReader.ReadBounded(catalog, new(), catalog.Count))
                    hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(record));
            }
            else
            {
                foreach (var file in dataset.GetProperty("files").EnumerateArray())
                {
                    var id = pack.Id + ":" + file.GetProperty("path").GetString();
                    var asset = context.GetAsset(id)!;
                    hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(asset.ContentType));
                    hash.AppendData(asset.Content.Span);
                    hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(context.AssetProvenance(id)));
                }
            }
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
