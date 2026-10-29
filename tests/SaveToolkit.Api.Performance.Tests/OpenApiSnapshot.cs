using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SaveToolkit.Api.Performance.Tests;

internal static class OpenApiSnapshot
{
    internal const string RelativePath = "tests/SaveToolkit.Api.Performance.Tests/OpenApi.v1.json";
    internal const string RepinCommand = "$env:SAVETOOLKIT_REPIN_OPENAPI = '1'; try { dotnet test " +
        "--project tests/SaveToolkit.Api.Performance.Tests -c Release " +
        "--filter-method '*Document_is_available_and_matches_the_contract' } finally { Remove-Item Env:SAVETOOLKIT_REPIN_OPENAPI }";

    internal static void AssertMatches(JsonObject document)
    {
        string path = Path.Combine(RepositoryRoot(), RelativePath);
        if (Environment.GetEnvironmentVariable("SAVETOOLKIT_REPIN_OPENAPI") == "1") Write(document);
        Assert.True(File.Exists(path), $"Missing {RelativePath}. Review and re-pin with: {RepinCommand}");
        string expected = File.ReadAllText(path).ReplaceLineEndings("\n");
        string actual = Normalize(document);
        Assert.True(expected == actual, $"OpenAPI contract changed. Review the diff and re-pin intentionally with: {RepinCommand}");
    }

    internal static void Write(JsonObject document) =>
        File.WriteAllText(Path.Combine(RepositoryRoot(), RelativePath), Normalize(document), new UTF8Encoding(false));

    internal static string Normalize(JsonObject document)
    {
        var copy = (JsonObject)document.DeepClone();
        // TestServer/host addresses vary; all paths, schemas, parameters and responses remain pinned.
        copy.Remove("servers");
        return Sort(copy)!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
    }

    private static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => KeyValuePair.Create(property.Key, Sort(property.Value)))),
        JsonArray array => new JsonArray(array.Select(Sort).ToArray()),
        JsonValue value when value.TryGetValue<string>(out var text) => JsonValue.Create(text.ReplaceLineEndings("\n")),
        _ => node?.DeepClone(),
    };

    private static string RepositoryRoot([CallerFilePath] string sourceFile = "")
    {
        // Redirected build output may live outside the checkout; keep the reviewed
        // snapshot tied to this test's source tree rather than the scratch drive.
        foreach (var start in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(sourceFile) })
        {
            var directory = start is null ? null : new DirectoryInfo(start);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SaveToolkit.slnx")))
                directory = directory.Parent;
            if (directory is not null) return directory.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate the checkout containing the reviewed OpenAPI snapshot.");
    }
}
