using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SaveToolkit.Api.Performance.Tests;

public sealed class OpenApiSchemaTests
{
    private const string DocumentPath = "/api/workspaces/{workspaceId}/documents/{documentId}";

    [Fact]
    public async Task Every_schema_reference_and_discriminator_target_resolves()
    {
        var document = await ReadDocument();
        foreach (var (name, value) in Properties(document))
        {
            if (name != "$ref" && value is not JsonValue) continue;
            if (value is not JsonValue scalar || !scalar.TryGetValue<string>(out string? reference)
                || !reference.StartsWith("#/components/schemas/", StringComparison.Ordinal)) continue;
            Assert.NotNull(document["components"]!["schemas"]![reference["#/components/schemas/".Length..]]);
        }
    }

    [Fact]
    public async Task Custom_binders_publish_every_path_parameter_and_collection_query()
    {
        var document = await ReadDocument();
        foreach (var (path, methods) in document["paths"]!.AsObject())
        foreach (var (_, method) in methods!.AsObject())
        {
            var parameters = method!["parameters"]?.AsArray() ?? [];
            foreach (Match placeholder in Regex.Matches(path, @"\{([^}]+)\}"))
                Assert.Contains(parameters, parameter => parameter!["name"]!.GetValue<string>() == placeholder.Groups[1].Value
                    && parameter["in"]!.GetValue<string>() == "path" && parameter["required"]!.GetValue<bool>());
            Assert.Equal(parameters.Count, parameters.Select(parameter => parameter!["in"] + ":" + parameter["name"]).Distinct().Count());
        }
        var queries = document["paths"]![DocumentPath + "/collections"]!["get"]!["parameters"]!.AsArray()
            .Where(parameter => parameter!["in"]!.GetValue<string>() == "query")
            .Select(parameter => parameter!["name"]!.GetValue<string>()).Order(StringComparer.Ordinal);
        Assert.Equal(["collectionId", "cursor", "descending", "filters", "includeEmpty", "limit", "offset", "q", "revision", "sort", "text"], queries);
    }

    [Fact]
    public async Task Binary_and_multipart_media_describe_the_existing_wire_format()
    {
        var document = await ReadDocument();
        var paths = document["paths"]!;
        foreach (var (path, method, media) in new[]
        {
            ("/api/workspaces/{workspaceId}/export", "post", "application/zip"),
            (DocumentPath + "/download", "get", "application/octet-stream"),
            (DocumentPath + "/blobs", "get", "application/octet-stream"),
            (DocumentPath + "/assets", "get", "*/*"),
        })
        {
            var schema = paths[path]![method]!["responses"]!["200"]!["content"]![media]!["schema"]!;
            Assert.Equal("string", schema["type"]!.GetValue<string>());
            Assert.Equal("binary", schema["format"]!.GetValue<string>());
        }
        foreach (string path in new[] { "/api/workspaces", "/api/reference-packs" })
            Assert.NotNull(paths[path]!["post"]!["requestBody"]!["content"]!["multipart/form-data"]);
    }

    [Fact]
    public async Task Document_failures_keep_problem_and_structured_result_metadata()
    {
        var document = await ReadDocument();
        var paths = document["paths"]!.AsObject();
        foreach (var (path, methods) in paths.Where(path => path.Key.StartsWith(DocumentPath, StringComparison.Ordinal)))
        foreach (var (_, method) in methods!.AsObject())
        {
            Assert.NotNull(method!["responses"]!["400"]);
            Assert.NotNull(method["responses"]!["412"]);
        }
        foreach (string action in new[] { "/edits", "/actions", "/actions/plan", "/actions/apply", "/undo", "/redo", "/changes/revert" })
        foreach (string status in new[] { "409", "422" })
        {
            var variants = paths[DocumentPath + action]!["post"]!["responses"]![status]!["content"]!["application/json"]!["schema"]!["anyOf"]!.AsArray();
            Assert.Equal(2, variants.Count);
            Assert.Equal(2, variants[1]!["allOf"]!.AsArray().Count);
        }
        var problem = document["components"]!["schemas"]!["OpenApiError"]!;
        Assert.DoesNotContain(problem["required"]!.AsArray(), item => item!.GetValue<string>() == "code");
    }

    [Fact]
    public async Task Converter_and_polymorphic_schemas_retain_values_and_field_bounds()
    {
        var document = await ReadDocument();
        var schemas = document["components"]!["schemas"]!;
        Assert.Equal(6, schemas["FieldValue"]!["anyOf"]!.AsArray().Count);
        var integer = schemas["IntegerSpec"]!;
        Assert.NotNull(integer);
        Assert.Equal("string", integer["properties"]!["min"]!["type"]!.GetValue<string>());
        Assert.Equal("string", integer["properties"]!["max"]!["type"]!.GetValue<string>());
        Assert.Equal(11, schemas["FieldSpec"]!["anyOf"]!.AsArray().Count);
        Assert.NotNull(schemas["TableNode"]!["properties"]!["rows"]!["items"]);
        Assert.DoesNotContain(schemas["ActionPreview"]!["required"]!.AsArray(), item => item!.GetValue<string>() == "planId");
    }

    [Fact]
    public void Normalization_ignores_host_property_order_and_line_endings_but_retains_contract_changes()
    {
        var original = JsonNode.Parse("""{"openapi":"3.1.0","description":"line\r\nnext","paths":{"/api/a":{"get":{"responses":{"200":{}}}}},"components":{"schemas":{"Example":{}}},"servers":[{"url":"http://localhost:1"}]}""")!.AsObject();
        var reordered = JsonNode.Parse("""{"components":{"schemas":{"Example":{}}},"paths":{"/api/a":{"get":{"responses":{"200":{}}}}},"description":"line\nnext","openapi":"3.1.0","servers":[{"url":"http://localhost:2"}]}""")!.AsObject();
        Assert.Equal(OpenApiSnapshot.Normalize(original), OpenApiSnapshot.Normalize(reordered));
        var changed = (JsonObject)original.DeepClone();
        changed["paths"]!["/api/a"]!["get"]!["responses"]!["404"] = new JsonObject();
        Assert.NotEqual(OpenApiSnapshot.Normalize(original), OpenApiSnapshot.Normalize(changed));
    }

    private static async Task<JsonObject> ReadDocument()
    {
        using var host = OpenApiContractTests.CreateHost("Production");
        using var client = host.CreateClient();
        return JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!.AsObject();
    }

    private static IEnumerable<KeyValuePair<string, JsonNode?>> Properties(JsonNode? node)
    {
        if (node is JsonObject obj)
            foreach (var property in obj)
            {
                yield return property;
                foreach (var nested in Properties(property.Value)) yield return nested;
            }
        if (node is JsonArray array)
            foreach (var item in array)
            foreach (var nested in Properties(item)) yield return nested;
    }
}
