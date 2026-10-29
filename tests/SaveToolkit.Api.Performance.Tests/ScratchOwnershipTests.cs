using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SaveToolkit.Testing;

namespace SaveToolkit.Api.Performance.Tests;

public sealed class ScratchOwnershipTests
{
    [Fact]
    public void OpenApiStorage_IsOwnedByProcessScratch()
    {
        using var host = OpenApiContractTests.CreateHost("Production");
        using var client = host.CreateClient();
        var configuration = host.Services.GetRequiredService<IConfiguration>();

        AssertOwned(configuration["Toolkit:DataRoot"]!);
        AssertOwned(configuration["Toolkit:PackRoot"]!);
    }

    [Fact]
    public void PerformanceHostStorage_IsOwnedByProcessScratch()
    {
        using var host = new ApiPerformanceHost();

        AssertOwned(host.StorageRoot);
    }

    private static void AssertOwned(string path) =>
        Assert.StartsWith(Path.GetFullPath(TestScratch.ProcessFolder) + Path.DirectorySeparatorChar,
            Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);
}
