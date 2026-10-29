using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SaveToolkit.Api.Infrastructure;
using SaveToolkit.Core.References;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests;

public sealed class ApiFactoryIsolationTests
{
    [Fact]
    public void IndependentHostsUseDistinctProcessOwnedWorkspaceAndPackStorage()
    {
        using var first = new IsolatedApiFactory();
        using var second = new IsolatedApiFactory();
        using var customized = first.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var firstClient = first.CreateClient();
        using var secondClient = second.CreateClient();
        using var customizedClient = customized.CreateClient();
        var firstOptions = first.Services.GetRequiredService<IOptions<ToolkitOptions>>().Value;
        var secondOptions = second.Services.GetRequiredService<IOptions<ToolkitOptions>>().Value;
        var customizedOptions = customized.Services.GetRequiredService<IOptions<ToolkitOptions>>().Value;
        string[] roots = [firstOptions.DataRoot, firstOptions.PackRoot, secondOptions.DataRoot,
            secondOptions.PackRoot, customizedOptions.DataRoot, customizedOptions.PackRoot];

        Assert.Equal(roots.Length, roots.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(roots, root => Assert.StartsWith(TestScratch.ProcessFolder + Path.DirectorySeparatorChar,
            root, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(firstOptions.PackRoot, first.Services.GetRequiredService<ReferencePackStore>().Options.StorageRoot);
        Assert.Equal(secondOptions.PackRoot, second.Services.GetRequiredService<ReferencePackStore>().Options.StorageRoot);
        Assert.Equal(customizedOptions.PackRoot, customized.Services.GetRequiredService<ReferencePackStore>().Options.StorageRoot);
    }
}
