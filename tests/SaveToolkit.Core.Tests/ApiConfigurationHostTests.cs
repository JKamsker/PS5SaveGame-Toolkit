using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;

namespace SaveToolkit.Core.Tests;

public sealed class ApiConfigurationHostTests
{
    [Theory]
    [InlineData(null, "en")]
    [InlineData("", "en")]
    [InlineData(" , , ", "")]
    [InlineData(" de, en, de ", "de,en,de")]
    public void UseSetting_PreservesStorageAndLocaleSemantics(string? locales, string expected)
    {
        string data = TestZips.TempDir(), packs = TestZips.TempDir();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("Toolkit:DataRoot", data).UseSetting("Toolkit:PackRoot", packs)
            .UseSetting("Toolkit:Locales", locales));
        using var client = factory.CreateClient();
        Assert.True(Directory.Exists(data));
        var options = factory.Services.GetRequiredService<ReferencePackStore>().Options;
        Assert.Equal(packs, options.StorageRoot);
        Assert.Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries), options.PreferredLocales);
    }

    [Fact]
    public void ConfigurationReload_DoesNotRebindSingletonStoresOrOptions()
    {
        string data = TestZips.TempDir(), packs = TestZips.TempDir();
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("Toolkit:DataRoot", data).UseSetting("Toolkit:PackRoot", packs).UseSetting("Toolkit:Locales", "de,en"));
        using var client = factory.CreateClient();
        var services = factory.Services;
        var store = services.GetRequiredService<WorkspaceStore>();
        var packStore = services.GetRequiredService<ReferencePackStore>();
        var options = services.GetRequiredService<IOptions<ToolkitOptions>>().Value;
        var config = services.GetRequiredService<IConfiguration>();
        string replacementData = TestZips.TempDir();
        config["Toolkit:DataRoot"] = replacementData;
        config["Toolkit:PackRoot"] = TestZips.TempDir();
        config["Toolkit:Locales"] = "fr";
        ((IConfigurationRoot)config).Reload();
        Assert.Same(store, services.GetRequiredService<WorkspaceStore>());
        Assert.Same(packStore, services.GetRequiredService<ReferencePackStore>());
        Assert.Same(options, services.GetRequiredService<IOptions<ToolkitOptions>>().Value);
        Assert.Equal(data, options.DataRoot);
        Assert.False(Directory.Exists(replacementData));
        Assert.Equal(packs, packStore.Options.StorageRoot);
        Assert.Equal(["de", "en"], packStore.Options.PreferredLocales);
    }

    [Theory]
    [InlineData("Toolkit:DataRoot")]
    [InlineData("Toolkit:PackRoot")]
    public void InvalidPath_FailsBeforeWorkspaceStartupChangesFiles(string key)
    {
        string data = TestZips.TempDir();
        Directory.CreateDirectory(data);
        string marker = Path.Combine(data, "untouched.txt");
        File.WriteAllText(marker, "preserve until configuration is valid");
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("Toolkit:DataRoot", data).UseSetting("Toolkit:PackRoot", TestZips.TempDir())
            .UseSetting(key, " "));
        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Equal([$"{key} must be a valid, non-whitespace directory path."], exception.Failures);
        Assert.True(File.Exists(marker));
    }

    [Fact]
    public async Task ExistingTransportLimits_AreUnchanged()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseSetting("Toolkit:DataRoot", TestZips.TempDir()).UseSetting("Toolkit:PackRoot", TestZips.TempDir()));
        using var client = factory.CreateClient();
        var forms = factory.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Features.FormOptions>>().Value;
        var kestrel = factory.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>>().Value;
        Assert.Equal(8L * 1024 * 1024 * 1024, forms.MultipartBodyLengthLimit);
        Assert.Equal(forms.MultipartBodyLengthLimit, kestrel.Limits.MaxRequestBodySize);
        Assert.Equal(100_000, forms.ValueCountLimit);
        using var response = await client.GetAsync(new Uri("/api/version", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
