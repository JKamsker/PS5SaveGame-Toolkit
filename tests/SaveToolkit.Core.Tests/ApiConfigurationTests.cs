using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SaveToolkit.Api.Infrastructure;

namespace SaveToolkit.Core.Tests;

public sealed class ApiConfigurationTests
{
    [Theory]
    [InlineData("appsettings")]
    [InlineData("environment")]
    [InlineData("commandLine")]
    public void EachSource_BindsAllThreeSettings(string source)
    {
        using var fixture = new ConfigurationSources();
        var config = new ConfigurationBuilder();
        if (source == "appsettings") config.AddJsonFile(fixture.File);
        if (source == "environment") config.AddEnvironmentVariables(fixture.Prefix);
        if (source == "commandLine") config.AddCommandLine(fixture.Arguments);
        using var configuration = (ConfigurationRoot)config.Build();
        using var services = OptionsServices(configuration);
        var options = services.GetRequiredService<IOptions<ToolkitOptions>>().Value;
        Assert.Equal(source + "/workspaces", options.DataRoot);
        Assert.Equal(source + "/packs", options.PackRoot);
        Assert.Equal([source, "en"], options.ToPackOptions().PreferredLocales);
        Assert.Equal(options.DataRoot, options.ToWorkspaceOptions().DataRoot);
        Assert.Equal(options.PackRoot, options.ToPackOptions().StorageRoot);
    }

    [Theory]
    [InlineData(false, false, "appsettings")]
    [InlineData(true, false, "environment")]
    [InlineData(true, true, "commandLine")]
    public void CommandLineOverridesEnvironmentWhichOverridesAppsettings(bool environment, bool commandLine, string expected)
    {
        using var fixture = new ConfigurationSources();
        var config = new ConfigurationBuilder().AddJsonFile(fixture.File);
        if (environment) config.AddEnvironmentVariables(fixture.Prefix);
        if (commandLine) config.AddCommandLine(fixture.Arguments);
        using var configuration = (ConfigurationRoot)config.Build();
        using var services = OptionsServices(configuration);
        var options = services.GetRequiredService<IOptions<ToolkitOptions>>().Value;
        Assert.Equal(expected + "/workspaces", options.DataRoot);
        Assert.Equal(expected + "/packs", options.PackRoot);
        Assert.Equal([expected, "en"], options.ToPackOptions().PreferredLocales);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingOrEmptyValues_UseExistingDefaults(string? value)
    {
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Toolkit:DataRoot"] = value, ["Toolkit:PackRoot"] = value, ["Toolkit:Locales"] = value,
        }).Build();
        using var services = OptionsServices(config);
        var options = services.GetRequiredService<IOptions<ToolkitOptions>>().Value;
        Assert.Equal(Path.Combine(Path.GetTempPath(), "SaveToolkit", "workspaces"), options.DataRoot);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SaveToolkit", "reference-packs"), options.PackRoot);
        Assert.Equal(["en"], options.ToPackOptions().PreferredLocales);
    }

    [Fact]
    public void AbsentSection_UsesExistingDefaults()
    {
        using var config = (ConfigurationRoot)new ConfigurationBuilder().Build();
        using var services = OptionsServices(config);
        var options = services.GetRequiredService<IOptions<ToolkitOptions>>().Value;
        Assert.Equal(Path.Combine(Path.GetTempPath(), "SaveToolkit", "workspaces"), options.DataRoot);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SaveToolkit", "reference-packs"), options.PackRoot);
        Assert.Equal("en", options.Locales);
    }

    [Fact]
    public void EmptyCommandLineOverrides_SelectDefaultsInsteadOfLowerPrecedenceValues()
    {
        using var fixture = new ConfigurationSources();
        using var config = (ConfigurationRoot)new ConfigurationBuilder().AddJsonFile(fixture.File)
            .AddEnvironmentVariables(fixture.Prefix)
            .AddCommandLine(["--Toolkit:DataRoot=", "--Toolkit:PackRoot=", "--Toolkit:Locales="]).Build();
        using var services = OptionsServices(config);
        var options = services.GetRequiredService<IOptions<ToolkitOptions>>().Value;
        Assert.Equal(Path.Combine(Path.GetTempPath(), "SaveToolkit", "workspaces"), options.DataRoot);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SaveToolkit", "reference-packs"), options.PackRoot);
        Assert.Equal(["en"], options.ToPackOptions().PreferredLocales);
    }

    [Theory]
    [InlineData("Toolkit:DataRoot", " ")]
    [InlineData("Toolkit:PackRoot", "\t")]
    [InlineData("Toolkit:DataRoot", "bad\0path")]
    [InlineData("Toolkit:PackRoot", "bad\0path")]
    public async Task InvalidPaths_FailAtStartupWithThePreciseSettingMessage(string key, string value)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [key] = value });
        builder.Services.AddToolkitOptions();
        using var host = builder.Build();
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            host.StartAsync(TestContext.Current.CancellationToken));
        Assert.Equal([$"{key} must be a valid, non-whitespace directory path."], exception.Failures);
    }

    [Fact]
    public async Task InvalidPaths_ReportBothSettingsTogether()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Toolkit:DataRoot"] = " ", ["Toolkit:PackRoot"] = "\t",
        });
        builder.Services.AddToolkitOptions();
        using var host = builder.Build();
        var exception = await Assert.ThrowsAsync<OptionsValidationException>(() =>
            host.StartAsync(TestContext.Current.CancellationToken));
        Assert.Equal([
            "Toolkit:DataRoot must be a valid, non-whitespace directory path.",
            "Toolkit:PackRoot must be a valid, non-whitespace directory path.",
        ], exception.Failures);
    }

    private static ServiceProvider OptionsServices(IConfiguration configuration) => new ServiceCollection()
        .AddSingleton(configuration).AddToolkitOptions().BuildServiceProvider();

    private sealed class ConfigurationSources : IDisposable
    {
        internal string Prefix { get; } = $"SAVETOOLKIT_CONFIG_TEST_{Guid.NewGuid():N}_";
        internal string File { get; }
        internal string[] Arguments { get; } = ["--Toolkit:DataRoot=commandLine/workspaces",
            "--Toolkit:PackRoot", "commandLine/packs", "--Toolkit:Locales", "commandLine,en"];

        internal ConfigurationSources()
        {
            string root = TestZips.TempDir();
            Directory.CreateDirectory(root);
            File = Path.Combine(root, "appsettings.json");
            System.IO.File.WriteAllText(File, JsonSerializer.Serialize(new
            {
                Toolkit = new { DataRoot = "appsettings/workspaces", PackRoot = "appsettings/packs", Locales = "appsettings,en" },
            }));
            Environment.SetEnvironmentVariable(Prefix + "Toolkit__DataRoot", "environment/workspaces");
            Environment.SetEnvironmentVariable(Prefix + "Toolkit__PackRoot", "environment/packs");
            Environment.SetEnvironmentVariable(Prefix + "Toolkit__Locales", "environment,en");
        }

        public void Dispose()
        {
            foreach (string key in new[] { "DataRoot", "PackRoot", "Locales" })
                Environment.SetEnvironmentVariable(Prefix + "Toolkit__" + key, null);
        }
    }
}
