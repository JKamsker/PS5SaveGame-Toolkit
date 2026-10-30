using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Presets;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests;

public sealed class PresetTransferBoundsApiTests(IsolatedApiFactory factory) : IClassFixture<IsolatedApiFactory>
{
    private const string Transfer = "/api/presets/transfer";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task All256MaximumIdsExportAndSpecificReplacementFitSeparateFiniteBounds(bool unicode)
    {
        var root = TestZips.TempDir();
        using var host = factory.WithWebHostBuilder(builder => builder
            .UseSetting("Toolkit:DataRoot", TestZips.TempDir()).UseSetting("Toolkit:PresetRoot", root));
        using var client = host.CreateClient();
        var store = host.Services.GetRequiredService<PresetStore>();
        var records = Enumerable.Range(0, PresetTransferLimits.MaxRecords).Select(index => Record(index, unicode)).ToArray();
        store.ImportLibrary(store.Snapshot().Identity, records);
        var exportRequest = "{" + Escaped("ids") + ":[" + string.Join(',', records.Select(p => Escaped(p.Id))) + "]}";
        Assert.InRange(Encoding.UTF8.GetByteCount(exportRequest), 32 * 1024 + 1, PresetTransferLimits.MaxControlBytes);
        using var export = await Post(client, "/export", exportRequest);
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal("application/json", export.Content.Headers.ContentType!.MediaType);
        var bytes = await export.Content.ReadAsByteArrayAsync(Ct);
        Assert.InRange(bytes.Length, 1, PresetPackageCodec.MaxBytes);
        Assert.Equal(256, PresetPackageCodec.Decode(bytes).Presets.Count);
        var changed = records.Select(p => p with { Actions = [Action(true)] }).ToArray();
        var package = PresetPackageCodec.Encode(changed);
        Assert.InRange(package.Length, 1, PresetPackageCodec.MaxBytes);
        using var upload = new ByteArrayContent(package);
        upload.Headers.ContentType = new("application/json");
        using var response = await client.PostAsync(Transfer + "/preview", upload, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = (await response.Content.ReadFromJsonAsync<PresetImportPreview>(Ct))!;
        Assert.All(preview.Items, item => Assert.Single(item.Conflicts));
        var choices = string.Join(',', records.Select((preset, index) => "{" + Escaped("index") + ":" + index.ToString(CultureInfo.InvariantCulture)
            + "," + Escaped("decision") + ":" + Escaped("replace") + "," + Escaped("replaceId") + ":" + Escaped(preset.Id) + "}"));
        var resolution = "{" + Escaped("token") + ":" + Escaped(preview.Token) + "," + Escaped("choices") + ":[" + choices + "]}";
        Assert.InRange(Encoding.UTF8.GetByteCount(resolution), 32 * 1024 + 1, PresetTransferLimits.MaxControlBytes);
        using var resolved = await Post(client, "/resolve", resolution);
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        var reviewed = (await resolved.Content.ReadFromJsonAsync<PresetImportPreview>(Ct))!;
        Assert.True(reviewed.ReadyToCommit);
        Assert.All(reviewed.Items, item => Assert.Equal(item.Name, item.ReplacesName));
        var path = Path.Combine(root, "presets.json");
        var before = File.ReadAllBytes(path);
        Directory.CreateDirectory(path + ".tmp");
        using var failed = await client.PostAsJsonAsync(Transfer + "/commit", new PresetImportCommit(reviewed.Token), Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.All(store.List(), preset => Assert.False(preset.Actions[0].Parameters["flag"].GetBoolean()));
        Directory.Delete(path + ".tmp");
        using var committed = await client.PostAsJsonAsync(Transfer + "/commit", new PresetImportCommit(reviewed.Token), Ct);
        Assert.Equal(HttpStatusCode.OK, committed.StatusCode);
        var result = (await committed.Content.ReadFromJsonAsync<PresetImportResult>(Ct))!;
        Assert.Equal(256, result.Imported.Count);
        var reloaded = new PresetStore(new() { StorageRoot = root }).List();
        Assert.Equal(256, reloaded.Count);
        Assert.All(reloaded, preset => Assert.True(preset.Actions[0].Parameters["flag"].GetBoolean()));
        using var hugeControl = new ByteArrayContent(new byte[PresetTransferLimits.MaxControlBytes + 1]);
        using var rejectedControl = await client.PostAsync(Transfer + "/export", hugeControl, Ct);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rejectedControl.StatusCode);
        using var hugePackage = new ByteArrayContent(new byte[PresetPackageCodec.MaxBytes + 1]);
        using var rejectedPackage = await client.PostAsync(Transfer + "/preview", hugePackage, Ct);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rejectedPackage.StatusCode);
        Assert.Equal(256, store.List().Count);
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string path, string json) =>
        client.PostAsync(Transfer + path, new StringContent(json, Encoding.UTF8, "application/json"), Ct);

    private static ToolkitPreset Record(int index, bool unicode)
    {
        var prefix = index.ToString("D3", CultureInfo.InvariantCulture) + (unicode ? "\"\\🧰é" : "id");
        var id = prefix + new string(unicode ? '界' : 'x', PresetTransferLimits.MaxPresetIdLength - prefix.Length);
        return new("1", id, "Record " + index.ToString(CultureInfo.InvariantCulture), new("unknown-later", "1", ["OTHER"]),
            [Action(false)], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    }

    private static PresetAction Action(bool flag) => new("flag", new Dictionary<string, JsonElement>
    {
        ["flag"] = JsonSerializer.SerializeToElement(flag),
    }, [], PresetScopeKinds.Document);

    // Force every property name and string code unit through its longest legal JSON form.
    private static string Escaped(string value) => "\"" + string.Concat(value.Select(unit => "\\u" + ((int)unit).ToString("x4", CultureInfo.InvariantCulture))) + "\"";
}
