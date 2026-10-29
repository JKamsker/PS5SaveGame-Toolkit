using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using SaveToolkit.Core.Presets;

namespace SaveToolkit.Core.Tests;

public sealed class PresetTransferMetadataApiTests(IsolatedApiFactory factory) : IClassFixture<IsolatedApiFactory>
{
    private const string Transfer = "/api/presets/transfer";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("gameBuild", false)]
    [InlineData("gameBuild", true)]
    [InlineData("formatVariant", false)]
    [InlineData("formatVariant", true)]
    [InlineData("referencePackIdentity", false)]
    [InlineData("referencePackIdentity", true)]
    [InlineData("schemaFingerprint", false)]
    [InlineData("schemaFingerprint", true)]
    [InlineData("fileRole", false)]
    [InlineData("fileRole", true)]
    [InlineData("capabilitySchemaVersion", false)]
    [InlineData("capabilitySchemaVersion", true)]
    [InlineData("path", false)]
    [InlineData("path", true)]
    [InlineData("provenance", false)]
    [InlineData("provenance", true)]
    public async Task EveryMetadataMaximumCanBeImportedAndOverflowConsumesNoPreview(string field, bool unicode)
    {
        var root = TestZips.TempDir();
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("Toolkit:PresetRoot", root));
        using var client = host.CreateClient();
        var value = unicode ? "🧰" + new string('界', PresetContractValidation.MaxMetadataLength - 2)
            : new string('x', PresetContractValidation.MaxMetadataLength);
        Assert.Equal(PresetContractValidation.MaxMetadataLength, value.Length);
        if (unicode) Assert.True(Encoding.UTF8.GetByteCount(value) > value.Length);
        var package = JsonNode.Parse(PresetPackageCodec.Encode([PresetTransferFixture.Preset()]))!;
        Set(package, field, value + "x");
        using var rejected = await Preview(client, package);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        var error = JsonNode.Parse(await rejected.Content.ReadAsStringAsync(Ct))!;
        Assert.Equal("presetPackageField", error["code"]!.GetValue<string>());
        Assert.Contains(field, error["error"]!.GetValue<string>());
        Assert.Contains(PresetContractValidation.MaxMetadataLength.ToString(System.Globalization.CultureInfo.InvariantCulture), error["error"]!.GetValue<string>());
        Assert.Null(error["token"]);
        Assert.Empty((await client.GetFromJsonAsync<JsonNode[]>("/api/presets", Ct))!);
        var path = Path.Combine(root, "presets.json");
        Assert.False(File.Exists(path));

        Set(package, field, value);
        var tokens = new List<string>();
        // Filling all eight slots proves that the rejected package retained no preview/token.
        for (var index = 0; index < 8; index++)
        {
            using var accepted = await Preview(client, package);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            tokens.Add((await accepted.Content.ReadFromJsonAsync<PresetImportPreview>(Ct))!.Token);
        }
        foreach (var token in tokens.Skip(1))
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(Transfer + "/previews/" + token, Ct)).StatusCode);
        using var resolve = await client.PostAsJsonAsync(Transfer + "/resolve",
            new PresetImportResolution(tokens[0], [new(0, "keepBoth")]), Ct);
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        var reviewed = (await resolve.Content.ReadFromJsonAsync<PresetImportPreview>(Ct))!;
        Assert.True(reviewed.ReadyToCommit);
        using var commit = await client.PostAsJsonAsync(Transfer + "/commit", new PresetImportCommit(reviewed.Token), Ct);
        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);
        var reopened = Assert.Single(new PresetStore(new() { StorageRoot = root }).List());
        var roundtrip = JsonNode.Parse(PresetPackageCodec.Encode([reopened]))!;
        Assert.Equal(value, Target(roundtrip, field)[field]!.GetValue<string>());
        Assert.Equal("900719925474099312345678901", reopened.Actions[0].Parameters["huge"].GetRawText());
        Assert.InRange(new FileInfo(path).Length, 1, PresetPackageCodec.MaxBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PackagePreviewAlsoUsesCommonIntrinsicStoreValidation(bool duplicateReference)
    {
        var root = TestZips.TempDir();
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("Toolkit:PresetRoot", root));
        using var client = host.CreateClient();
        var package = JsonNode.Parse(PresetPackageCodec.Encode([PresetTransferFixture.Preset()]))!;
        var action = package["presets"]![0]!["actions"]![0]!;
        if (duplicateReference)
            action["instances"]!.AsArray().Add(action["instances"]![0]!.DeepClone());
        else
            action["parameters"] = new JsonObject(Enumerable.Range(0, 65).Select(index =>
                KeyValuePair.Create<string, JsonNode?>("p" + index, JsonValue.Create(new string('x', 4096)))));
        using var rejected = await Preview(client, package);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        var error = JsonNode.Parse(await rejected.Content.ReadAsStringAsync(Ct))!;
        Assert.Equal("invalidPresetLibrary", error["code"]!.GetValue<string>());
        Assert.Null(error["token"]);
        Assert.Empty((await client.GetFromJsonAsync<JsonNode[]>("/api/presets", Ct))!);
        Assert.False(File.Exists(Path.Combine(root, "presets.json")));
    }

    private static Task<HttpResponseMessage> Preview(HttpClient client, JsonNode package) =>
        client.PostAsync(Transfer + "/preview", new StringContent(package.ToJsonString(), Encoding.UTF8, "application/json"), Ct);

    private static JsonNode Target(JsonNode package, string field) => field is "path" or "provenance"
        ? package["presets"]![0]!["actions"]![0]!["instances"]![0]!
        : package["presets"]![0]!["compatibility"]!;

    private static void Set(JsonNode package, string field, string value) => Target(package, field)[field] = value;
}
