using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SaveToolkit.Api.Contracts;
using SaveToolkit.Core.Presets;

namespace SaveToolkit.Api.Performance.Tests;

public sealed class PresetTransferOpenApiTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task TransferGraphRequiresExactEnvelopeWhileLegacyCrudStaysOptional()
    {
        using var host = OpenApiContractTests.CreateHost("Production");
        using var client = host.CreateClient();
        var document = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json", Ct))!;
        var schemas = document["components"]!["schemas"]!;
        AssertRequired(schemas["PresetPackageDto"]!, "schemaVersion", "provenance", "presets");
        AssertRequired(schemas["PresetPackageProvenanceDto"]!, "producer", "exportedAt");
        AssertRequired(schemas["PresetPackageRecordDto"]!, "schemaVersion", "id", "name", "compatibility", "actions", "createdAt", "updatedAt");
        var compatibility = schemas["PresetPackageCompatibilityDto"]!;
        AssertRequired(compatibility, "pluginId", "pluginVersion", "titleIds", "gameBuild", "formatVariant", "referencePackIdentity",
            "requiresExplicitInstanceResolution", "presetKind", "schemaFingerprint", "fileRole", "capabilitySchemaVersion");
        foreach (var field in new[] { "gameBuild", "formatVariant", "referencePackIdentity", "schemaFingerprint", "fileRole", "capabilitySchemaVersion" })
        {
            var types = compatibility["properties"]![field]!["type"]!.AsArray().Select(type => type!.GetValue<string>());
            Assert.Contains("null", types);
            Assert.Contains("string", types);
        }
        AssertRequired(schemas["PresetPackageActionDto"]!, "actionId", "parameters", "instances", "scopeKind");
        Assert.Equal("^1$", schemas["PresetPackageDto"]!["properties"]!["schemaVersion"]!["pattern"]!.GetValue<string>());
        Assert.Equal(256, schemas["PresetPackageDto"]!["properties"]!["presets"]!["maxItems"]!.GetValue<int>());
        Assert.Equal(1, schemas["PresetPackageDto"]!["properties"]!["presets"]!["minItems"]!.GetValue<int>());
        Assert.Equal(128, schemas["PresetPackageRecordDto"]!["properties"]!["id"]!["maxLength"]!.GetValue<int>());
        Assert.Equal(256, schemas["PresetPackageActionDto"]!["properties"]!["instances"]!["maxItems"]!.GetValue<int>());
        Assert.Equal(128, schemas["PresetPackageActionDto"]!["properties"]!["parameters"]!["maxProperties"]!.GetValue<int>());
        AssertRequired(schemas["PresetCompatibility"]!, "pluginId", "pluginVersion", "titleIds");
        AssertRequired(schemas["PresetAction"]!, "actionId", "parameters");
        var body = document["paths"]!["/api/presets/transfer/preview"]!["post"]!["requestBody"]!["content"]!["application/json"]!["schema"]!;
        Assert.EndsWith("/PresetPackageDto", body["$ref"]!.GetValue<string>());
    }

    [Fact]
    public async Task Cancel204AndExportJsonDownloadBodyAreExplicit()
    {
        using var host = OpenApiContractTests.CreateHost("Production");
        using var client = host.CreateClient();
        var document = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json", Ct))!;
        var cancel = document["paths"]!["/api/presets/transfer/previews/{token}"]!["delete"]!["responses"]!["204"]!;
        Assert.NotNull(cancel);
        Assert.Null(cancel["content"]);
        var body = document["paths"]!["/api/presets/transfer/export"]!["post"]!["responses"]!["200"]!["content"]!["application/json"]!["schema"]!;
        Assert.Equal("string", body["type"]!.GetValue<string>());
        Assert.Equal("binary", body["format"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/presets/transfer/previews/unknown", Ct)).StatusCode);
    }

    [Fact]
    public async Task TypedSchemaPackageWithExplicitNullAndEmptyInstancesIsAcceptedAndEveryRequiredOmissionRejected()
    {
        using var host = OpenApiContractTests.CreateHost("Production");
        using var client = host.CreateClient();
        var dto = new PresetPackageDto("1", new("Fixture", DateTimeOffset.UnixEpoch),
        [new("1", "sample", "Sample", new("unknown", "1", ["OTHER"], null, null, null, true, "toolkit.action-parameters", null, null, null),
            [new("semantic", new Dictionary<string, JsonElement> { ["amount"] = JsonSerializer.SerializeToElement(900719925474099312345678901m) }, [], "document")],
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)]);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(dto, JsonOptions);
        var node = JsonNode.Parse(bytes)!;
        Assert.True(node["presets"]![0]!["compatibility"]!.AsObject().ContainsKey("gameBuild"));
        Assert.Null(node["presets"]![0]!["compatibility"]!["gameBuild"]);
        Assert.Empty(node["presets"]![0]!["actions"]![0]!["instances"]!.AsArray());
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new("application/json");
        using var accepted = await client.PostAsync("/api/presets/transfer/preview", content, Ct);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal("900719925474099312345678901", PresetPackageCodec.Decode(bytes).Presets[0].Actions[0].Parameters["amount"].GetRawText());
        var schema = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json", Ct))!["components"]!["schemas"]!;
        foreach (var name in new[] { "PresetPackageDto", "PresetPackageRecordDto", "PresetPackageCompatibilityDto", "PresetPackageActionDto" })
        foreach (var required in schema[name]!["required"]!.AsArray())
        {
            var invalid = node.DeepClone();
            var target = name switch
            {
                "PresetPackageRecordDto" => invalid["presets"]![0]!,
                "PresetPackageCompatibilityDto" => invalid["presets"]![0]!["compatibility"]!,
                "PresetPackageActionDto" => invalid["presets"]![0]!["actions"]![0]!,
                _ => invalid,
            };
            target.AsObject().Remove(required!.GetValue<string>());
            using var request = new StringContent(invalid.ToJsonString(), Encoding.UTF8, "application/json");
            using var rejected = await client.PostAsync("/api/presets/transfer/preview", request, Ct);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
            var error = await rejected.Content.ReadFromJsonAsync<JsonElement>(Ct);
            Assert.Equal("presetPackageMissingField", error.GetProperty("code").GetString());
        }
    }

    private static void AssertRequired(JsonNode schema, params string[] fields) => Assert.Equal(fields.Order(StringComparer.Ordinal),
        schema["required"]!.AsArray().Select(value => value!.GetValue<string>()).Order(StringComparer.Ordinal));
}
