using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SaveToolkit.Core.Tests.Fakes;

namespace SaveToolkit.Core.Tests;

public sealed class CleanDocumentCloseApiTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Acknowledged_write_before_delayed_delete_returns_conflict_and_preserves_the_live_document(bool grouped, bool redo)
    {
        using var fixture = new CleanCloseApiFixture(grouped, "DELETE");
        var pause = Assert.IsType<CleanCloseApiFixture.RequestPause>(fixture.Pause);
        var core = fixture.Core;
        if (redo)
        {
            Assert.True(core.Edit().Ok);
            Assert.True(core.Session.Undo().Ok);
        }
        var close = fixture.Client.DeleteAsync(new Uri(fixture.DocumentPath, UriKind.Relative), ApiContractFixture.Ct);
        try
        {
            await TestWorker.WaitForSignalAsync(pause.Entered.Task, close, ApiContractFixture.Ct);
            using var written = await WriteAsync(fixture, redo);
            written.EnsureSuccessStatusCode();
            var receipt = await written.Content.ReadFromJsonAsync<JsonElement>(ApiContractFixture.Ct);
            Assert.True(receipt.GetProperty("state").GetProperty("isDirty").GetBoolean());
            var state = core.Session.State;
            var outputs = core.Session.Serialize();
            pause.Resume.TrySetResult();

            using var response = await close;
            var problem = await ApiProblemContractTests.AssertProblemAsync(response, HttpStatusCode.Conflict);

            Assert.Contains("unsaved edits", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
            core.AssertRetained(state, outputs);
            using var stillOpen = await fixture.Client.GetAsync(new Uri(fixture.DocumentPath, UriKind.Relative), ApiContractFixture.Ct);
            stillOpen.EnsureSuccessStatusCode();
            Assert.True(core.Session.Undo().Ok);
            using var retry = await fixture.Client.DeleteAsync(new Uri(fixture.DocumentPath, UriKind.Relative), ApiContractFixture.Ct);
            Assert.Equal(HttpStatusCode.NoContent, retry.StatusCode);
            core.AssertClosed();
        }
        finally
        {
            pause.Resume.TrySetResult();
            using var completed = await close;
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Close_before_queued_write_rejects_it_without_acknowledging_a_disposed_session(bool grouped, bool redo)
    {
        using var fixture = new CleanCloseApiFixture(grouped, "POST", redo ? "/redo" : "/edits");
        var pause = Assert.IsType<CleanCloseApiFixture.RequestPause>(fixture.Pause);
        if (redo)
        {
            Assert.True(fixture.Core.Edit().Ok);
            Assert.True(fixture.Core.Session.Undo().Ok);
        }
        var write = WriteAsync(fixture, redo);
        try
        {
            await TestWorker.WaitForSignalAsync(pause.Entered.Task, write, ApiContractFixture.Ct);
            using var closed = await fixture.Client.DeleteAsync(new Uri(fixture.DocumentPath, UriKind.Relative), ApiContractFixture.Ct);
            Assert.Equal(HttpStatusCode.NoContent, closed.StatusCode);
            pause.Resume.TrySetResult();

            using var rejected = await write;

            await ApiProblemContractTests.AssertProblemAsync(rejected, HttpStatusCode.NotFound);
            fixture.Core.AssertClosed();
        }
        finally
        {
            pause.Resume.TrySetResult();
            using var completed = await write;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Clean_delete_succeeds_and_missing_document_or_workspace_is_404(bool grouped)
    {
        using var fixture = new CleanCloseApiFixture(grouped);
        using var response = await fixture.Client.DeleteAsync(new Uri(fixture.DocumentPath, UriKind.Relative), ApiContractFixture.Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(ApiContractFixture.Ct));
        using var missing = await fixture.Client.DeleteAsync(new Uri(fixture.DocumentPath, UriKind.Relative), ApiContractFixture.Ct);
        await ApiProblemContractTests.AssertProblemAsync(missing, HttpStatusCode.NotFound);
        using var absentWorkspace = await fixture.Client.DeleteAsync(new Uri("/api/workspaces/missing/documents/missing", UriKind.Relative), ApiContractFixture.Ct);
        await ApiProblemContractTests.AssertProblemAsync(absentWorkspace, HttpStatusCode.NotFound);
        fixture.Core.AssertClosed();
    }

    [Fact]
    public async Task Revision_precondition_does_not_replace_the_atomic_clean_state_requirement()
    {
        using var fixture = new CleanCloseApiFixture(grouped: false);
        Assert.True(fixture.Core.Edit().Ok);
        using var staleRequest = new HttpRequestMessage(HttpMethod.Delete, new Uri(fixture.DocumentPath, UriKind.Relative));
        staleRequest.Headers.TryAddWithoutValidation("If-Match", "\"0\"");
        using var stale = await fixture.Client.SendAsync(staleRequest, ApiContractFixture.Ct);
        await ApiProblemContractTests.AssertProblemAsync(stale, HttpStatusCode.PreconditionFailed);
        using var currentRequest = new HttpRequestMessage(HttpMethod.Delete, new Uri(fixture.DocumentPath, UriKind.Relative));
        currentRequest.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        using var dirty = await fixture.Client.SendAsync(currentRequest, ApiContractFixture.Ct);
        await ApiProblemContractTests.AssertProblemAsync(dirty, HttpStatusCode.Conflict);
        Assert.True(fixture.Core.Session.State.IsDirty);
        Assert.True(fixture.Core.Session.Undo().Ok);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_dump_delete_still_discards_dirty_work(bool grouped)
    {
        using var fixture = new CleanCloseApiFixture(grouped);
        Assert.True(fixture.Core.Edit().Ok);
        using var response = await fixture.Client.DeleteAsync(
            new Uri($"/api/workspaces/{fixture.Core.Workspace.Id}", UriKind.Relative), ApiContractFixture.Ct);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(fixture.Core.Store.All);
        Assert.False(Directory.Exists(fixture.Core.Workspace.Set.StorageRoot));
        Assert.Throws<ObjectDisposedException>(() => fixture.Core.Session.BuildView());
    }

    private static Task<HttpResponseMessage> WriteAsync(CleanCloseApiFixture fixture, bool redo)
    {
        object request = redo ? new { revision = fixture.Core.Session.State.Revision } : new
        {
            revision = fixture.Core.Session.State.Revision,
            edits = new[] { new { valueId = fixture.Core.Field, value = fixture.Core.EditedValue } },
        };
        return fixture.Client.PostAsJsonAsync(fixture.DocumentPath + (redo ? "/redo" : "/edits"), request, ApiContractFixture.Ct);
    }
}
