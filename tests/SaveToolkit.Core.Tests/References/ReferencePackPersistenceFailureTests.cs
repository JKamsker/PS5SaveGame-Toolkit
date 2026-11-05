using System.Net;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Core.References;

namespace SaveToolkit.Core.Tests.References;

public sealed class ReferencePackPersistenceFailureTests
{
    [Fact]
    public async Task FailedRename_DoesNotLeaveStagedArchiveOrPublishPack()
    {
        using var factory = new IsolatedApiFactory();
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<ReferencePackStore>();
        var root = store.Options.StorageRoot!;
        // A directory at the destination reliably makes the final atomic rename fail.
        Directory.CreateDirectory(Path.Combine(root, "pack-user.items.zip"));
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(PackBuilder.WithItems().Zip()), "file", "pack.zip");
        using var response = await client.PostAsync(new Uri("/api/reference-packs", UriKind.Relative), form,
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Null(store.Find("user.items"));
        Assert.Empty(Directory.EnumerateFiles(root));
    }
}
