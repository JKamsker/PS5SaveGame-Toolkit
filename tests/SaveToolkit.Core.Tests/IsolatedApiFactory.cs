using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace SaveToolkit.Core.Tests;

/// <summary>Keeps API tests independent of the application's default workspace and user-pack storage.</summary>
public sealed class IsolatedApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Toolkit:DataRoot", TestZips.TempDir())
            .UseSetting("Toolkit:PackRoot", TestZips.TempDir())
            .UseSetting("Toolkit:PresetRoot", TestZips.TempDir());
    }
}
