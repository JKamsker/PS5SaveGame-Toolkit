using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SaveToolkit.Core.Tests;

public sealed class FieldReferenceOpeningTests
{
    [Fact]
    public async Task FailedSingleOpenLeavesNoSessionAndAllowsRetryOrAnotherOwner()
    {
        var failing = new FieldReferenceOpeningPlugin("test.large") { OversizedSingle = true };
        var other = new FieldReferenceOpeningPlugin("test.other");
        using var fixture = new ApiContractFixture(failing, other);
        var (workspace, file) = await Upload(fixture);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await Open(fixture, workspace, file, failing.Info.Id);
            await ApiProblemContractTests.AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge);
            await AssertOwners(fixture, workspace);
            Assert.All(failing.Opened, document => Assert.True(document.IsDisposed));
        }
        using var alternate = await Open(fixture, workspace, file, other.Info.Id);
        alternate.EnsureSuccessStatusCode();
        var owner = await Id(alternate);
        using var replacement = await Open(fixture, workspace, file, failing.Info.Id);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, replacement.StatusCode);
        await AssertOwners(fixture, workspace, owner);
        Assert.False(other.Opened[0].IsDisposed);
        using var close = await fixture.Client.DeleteAsync($"{workspace}/documents/{owner}", ApiContractFixture.Ct);
        close.EnsureSuccessStatusCode();
        failing.OversizedSingle = false;
        using var retry = await Open(fixture, workspace, file, failing.Info.Id);
        retry.EnsureSuccessStatusCode();
        Assert.Equal(1, failing.Opened[^1].Views); // The API returns the pre-publication snapshot.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedGroupConfirmationKeepsOfferAndPriorOwnerAndReleasesCandidate(bool releaseCleanMember)
    {
        var plugin = new FieldReferenceOpeningPlugin("test.group-large") { OversizedGroup = true };
        using var fixture = new ApiContractFixture(plugin);
        var (workspace, file) = await Upload(fixture);
        string? owner = null;
        if (releaseCleanMember)
        {
            using var single = await Open(fixture, workspace, file, plugin.Info.Id);
            single.EnsureSuccessStatusCode();
            owner = await Id(single);
        }
        var offers = await fixture.Client.GetFromJsonAsync<JsonElement>($"{workspace}/files/{file}/group-proposals?pluginId={plugin.Info.Id}", ApiContractFixture.Ct);
        var request = new { proposalToken = offers[0].GetProperty("token").GetString(), releaseCleanDocumentId = owner };
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var rejected = await fixture.Client.PostAsJsonAsync(workspace + "/document-groups", request, ApiContractFixture.Ct);
            await ApiProblemContractTests.AssertProblemAsync(rejected, HttpStatusCode.RequestEntityTooLarge);
            await AssertOwners(fixture, workspace, owner);
            Assert.All(plugin.Opened.Skip(owner is null ? 0 : 1), document => Assert.True(document.IsDisposed));
            if (owner is not null) Assert.False(plugin.Opened[0].IsDisposed);
            else Assert.All(plugin.Opened, document => Assert.True(document.IsDisposed));
        }
        if (owner is null)
        {
            using var alternate = await Open(fixture, workspace, file, plugin.Info.Id);
            alternate.EnsureSuccessStatusCode();
            var alternateOwner = await Id(alternate);
            using var close = await fixture.Client.DeleteAsync($"{workspace}/documents/{alternateOwner}", ApiContractFixture.Ct);
            close.EnsureSuccessStatusCode();
        }
        plugin.OversizedGroup = false;
        using var retry = await fixture.Client.PostAsJsonAsync(workspace + "/document-groups", request, ApiContractFixture.Ct);
        retry.EnsureSuccessStatusCode();
        var group = await Id(retry);
        await AssertOwners(fixture, workspace, group, grouped: true);
        Assert.Equal(1, Assert.Single(plugin.Opened, document => !document.IsDisposed).Views);
        if (owner is not null) Assert.True(plugin.Opened[0].IsDisposed);
    }

    private static async Task<(string Workspace, string File)> Upload(ApiContractFixture fixture)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(TestZips.Build(("PPSA99990/sdimg_SAVE/first.dat", new byte[] { 1 }),
            ("PPSA99990/sdimg_SAVE/second.dat", new byte[] { 2 }))), "files", "pair.zip");
        using var uploaded = await fixture.Client.PostAsync("/api/workspaces", form, ApiContractFixture.Ct);
        uploaded.EnsureSuccessStatusCode();
        var workspace = await uploaded.Content.ReadFromJsonAsync<JsonElement>(ApiContractFixture.Ct);
        var file = workspace.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files")
            .EnumerateArray().Single(file => file.GetProperty("path").GetString() == "first.dat");
        return ($"/api/workspaces/{workspace.GetProperty("id").GetString()}", file.GetProperty("id").GetString()!);
    }

    private static Task<HttpResponseMessage> Open(ApiContractFixture fixture, string workspace, string file, string plugin) =>
        fixture.Client.PostAsJsonAsync($"{workspace}/files/{file}/open", new { pluginId = plugin }, ApiContractFixture.Ct);
    private static async Task<string> Id(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(ApiContractFixture.Ct)).GetProperty("id").GetString()!;
    private static async Task AssertOwners(ApiContractFixture fixture, string workspace, string? owner = null, bool grouped = false)
    {
        var current = await fixture.Client.GetFromJsonAsync<JsonElement>(workspace, ApiContractFixture.Ct);
        var documents = current.GetProperty("documents");
        Assert.Equal(owner is null ? 0 : 1, documents.GetArrayLength());
        if (owner is not null) Assert.Equal(owner, documents[0].GetProperty("id").GetString());
        foreach (var file in current.GetProperty("titles")[0].GetProperty("directories")[0].GetProperty("files").EnumerateArray())
        {
            var expected = grouped || file.GetProperty("path").GetString() == "first.dat" ? owner : null;
            Assert.Equal(expected, file.TryGetProperty("openDocumentId", out var open) ? open.GetString() : null);
        }
    }
}
