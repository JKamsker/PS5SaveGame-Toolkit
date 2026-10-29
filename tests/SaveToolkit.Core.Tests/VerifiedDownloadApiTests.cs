using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class VerifiedDownloadApiTests
{
    [Fact]
    public async Task Invalid_dirty_output_returns_problem_without_attachment_and_preserves_history()
    {
        var plugin = new DownloadPlugin { Outputs = outputs => [outputs[0] with { Content = new byte[] { 0 } }] };
        using var fixture = new ApiContractFixture(plugin);
        var (_, document) = await fixture.OpenAsync();
        using var edit = await fixture.PostAsync(document + "/edits",
            "{\"edits\":[{\"valueId\":\"counter\",\"value\":6}],\"revision\":0}");
        edit.EnsureSuccessStatusCode();
        var before = await fixture.Client.GetFromJsonAsync<JsonElement>(document, ApiContractFixture.Ct);

        using var response = await fixture.Client.GetAsync(new Uri(document + "/download", UriKind.Relative), ApiContractFixture.Ct);

        var problem = await ApiProblemContractTests.AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity);
        Assert.Null(response.Content.Headers.ContentDisposition);
        Assert.Contains(plugin.Info.Name, problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Contains("保存.dat", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(1, plugin.SerializeCalls);
        var after = await fixture.Client.GetFromJsonAsync<JsonElement>(document, ApiContractFixture.Ct);
        Assert.Equal(before.GetRawText(), after.GetRawText());
        using var undo = await fixture.PostAsync(document + "/undo", "{\"revision\":1}");
        undo.EnsureSuccessStatusCode();
        var undone = await undo.Content.ReadFromJsonAsync<JsonElement>(ApiContractFixture.Ct);
        Assert.False(undone.GetProperty("state").GetProperty("isDirty").GetBoolean());
        Assert.Equal(2, undone.GetProperty("state").GetProperty("revision").GetInt64());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Clean_and_read_only_downloads_do_not_serialize_or_reopen(bool readOnly)
    {
        var plugin = new DownloadPlugin { CanEdit = !readOnly };
        using var fixture = new ApiContractFixture(plugin);
        var (_, document) = await fixture.OpenAsync();
        plugin.OnOpen = (_, _) => throw new InvalidOperationException("Clean downloads must stream originals.");

        using var response = await fixture.Client.GetAsync(new Uri(document + "/download", UriKind.Relative), ApiContractFixture.Ct);

        response.EnsureSuccessStatusCode();
        Assert.Equal("保存.dat", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(CounterPlugin.Create(5), await response.Content.ReadAsByteArrayAsync(ApiContractFixture.Ct));
        Assert.Equal(0, plugin.SerializeCalls);
        Assert.Equal(1, plugin.OpenCalls);
    }

    [Fact]
    public async Task Cancellation_during_verification_returns_499_without_attachment_or_revision_change()
    {
        var plugin = new DownloadPlugin();
        using var fixture = new ApiContractFixture(plugin);
        var (_, document) = await fixture.OpenAsync();
        using var edit = await fixture.PostAsync(document + "/edits",
            "{\"edits\":[{\"valueId\":\"counter\",\"value\":6}],\"revision\":0}");
        edit.EnsureSuccessStatusCode();
        plugin.OnOpen = (_, _) => throw new OperationCanceledException();

        using var response = await fixture.Client.GetAsync(new Uri(document + "/download", UriKind.Relative), ApiContractFixture.Ct);

        await ApiProblemContractTests.AssertProblemAsync(response, (HttpStatusCode)499);
        Assert.Null(response.Content.Headers.ContentDisposition);
        plugin.OnOpen = null;
        using var undo = await fixture.PostAsync(document + "/undo", "{\"revision\":1}");
        undo.EnsureSuccessStatusCode();
    }
}
