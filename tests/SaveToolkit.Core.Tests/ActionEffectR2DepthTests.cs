using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Tests;

public sealed class ActionEffectR2DepthTests(IsolatedApiFactory factory) : IClassFixture<IsolatedApiFactory>
{
    [Fact]
    public void DefaultLegacyPlanBytesRemainAnExactGoldenWithEffectOmitted()
    {
        var plan = new ActionPlan { Changes = [new("a", "A", FieldValue.Of(false), FieldValue.Of(true))] };
        const string expected = """{"Changes":[{"ValueId":"a","Label":"A","OldValue":false,"NewValue":true,"Derived":false,"OldExists":true,"NewExists":true}],"SkippedRows":[],"Warnings":[],"Errors":[]}""";
        Assert.Equal(expected, JsonSerializer.Serialize(plan));
        Assert.Equal(expected, JsonSerializer.Serialize(ActionPlanSnapshots.Snapshot(plan), MetadataJson.Options));
    }

    [Fact]
    public async Task AdmittedDepth128PreviewIsWrittenByActualHttpJsonOptions()
    {
        var document = new ActionEffectR2Document { EffectSpec = DeepSpec(128, union: false) };
        using var host = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton(new PluginCatalog([new ActionEffectR2Plugin(document)]))));
        using var client = host.CreateClient();
        var ct = TestContext.Current.CancellationToken;
        var zip = TestZips.Build(("PPSA99999/sdimg_SAVE/save.dat", new byte[] { 0 }));
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(zip), "files", "dump.zip");
        using var upload = await client.PostAsync(new Uri("/api/workspaces", UriKind.Relative), form, ct);
        upload.EnsureSuccessStatusCode();
        var workspace = await upload.Content.ReadFromJsonAsync<JsonElement>(ct);
        var workspaceId = workspace.GetProperty("id").GetString();
        var fileId = workspace.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")[0].GetProperty("id").GetString();
        using var opened = await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/files/{fileId}/open", new { pluginId = "test.effect-r2" }, ct);
        opened.EnsureSuccessStatusCode();
        var openedBody = await opened.Content.ReadFromJsonAsync<JsonElement>(ct);
        var url = $"/api/workspaces/{workspaceId}/documents/{openedBody.GetProperty("id").GetString()}";
        using var response = await client.PostAsJsonAsync($"{url}/actions/plan", new { actionId = "change", revision = 0 }, ct);
        var bytes = await response.Content.ReadAsByteArrayAsync(ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var parsed = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = MetadataJson.MaxDepth });
        Assert.False(string.IsNullOrEmpty(parsed.RootElement.GetProperty("planId").GetString()));
        var spec = parsed.RootElement.GetProperty("plan").GetProperty("changes")[0].GetProperty("effect").GetProperty("spec");
        for (var index = 0; index < 128; index++)
        {
            Assert.Equal("nullable", spec.GetProperty("kind").GetString());
            spec = spec.GetProperty("inner");
        }
        Assert.Equal("integer", spec.GetProperty("kind").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentlyAllocatedDepth128SpecsCompareAndSerializeWithOuterWrappers(bool union)
    {
        static ActionPlan Plan(FieldSpec spec) => new()
        {
            Changes = [new PlannedChange("effect", "Effect", FieldValue.Null, FieldValue.Of(1))
            { OldExists = false, Effect = new(ActionEffectKind.StructuralMemento, spec) }],
        };
        var first = ActionPlanSnapshots.Snapshot(Plan(DeepSpec(128, union)));
        var second = ActionPlanSnapshots.Snapshot(Plan(DeepSpec(128, union)));
        Assert.True(ActionPlans.ContentEquals(first, second));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { preview = new { plan = first } }, ToolkitJson.Options);
        Assert.NotEmpty(bytes);
        Assert.Throws<InvalidOperationException>(() => ActionPlanSnapshots.Snapshot(Plan(DeepSpec(129, union))));
    }

    internal static FieldSpec DeepSpec(int depth, bool union)
    {
        FieldSpec spec = IntegerSpec.Int32;
        for (var index = 0; index < depth; index++)
            spec = union ? new UnionSpec([new("nested", "Nested", spec)]) : new NullableSpec(spec);
        return spec with { ReadOnly = true };
    }
}
