using System.Text.Json.Nodes;

namespace SaveToolkit.Api.Performance.Tests;

public sealed class PresetResponseOpenApiTests
{
    [Fact]
    public async Task PreviewAndApplyDescribeSuccessAndRetainedErrorBodies()
    {
        using var host = OpenApiContractTests.CreateHost("Production");
        using var client = host.CreateClient();
        var document = JsonNode.Parse(await client.GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken))!;
        const string root = "/api/workspaces/{workspaceId}/documents/{documentId}/presets/{presetId}";
        var preview = document["paths"]![root + "/preview"]!["post"]!["responses"]!;
        var apply = document["paths"]![root + "/apply"]!["post"]!["responses"]!;
        Assert.NotNull(preview["200"]);
        Assert.NotNull(apply["200"]);
        var capture = document["paths"]!["/api/workspaces/{workspaceId}/documents/{documentId}/presets/capture"]!["post"]!["responses"]!;
        Assert.NotNull(capture["200"]);
        Assert.EndsWith("/PresetDto", Schema(capture, "200")["$ref"]!.GetValue<string>());
        var variants = Schema(preview, "200")["anyOf"]!.AsArray();
        Assert.Equal(2, variants.Count);
        Assert.Contains(variants, variant => variant!["$ref"]!.GetValue<string>().EndsWith("/PresetPreviewDto", StringComparison.Ordinal));
        Assert.Contains(variants, variant => variant!["$ref"]!.GetValue<string>().EndsWith("/PresetSemanticPreviewDto", StringComparison.Ordinal));
        Assert.EndsWith("/SessionEditResult", Schema(apply, "200")["$ref"]!.GetValue<string>());
        foreach (var responses in new[] { preview, apply })
        foreach (var status in new[] { "409", "422" })
        {
            var alternatives = Schema(responses, status)["anyOf"]!.AsArray();
            Assert.Equal(2, alternatives.Count);
            Assert.True(JsonNode.DeepEquals(Schema(responses, "200"), alternatives[1]!["allOf"]![1]));
        }
    }

    private static JsonNode Schema(JsonNode responses, string status) =>
        responses[status]!["content"]!["application/json"]!["schema"]!;
}
