namespace SaveToolkit.Api.Infrastructure;

internal static class ToolkitOptionsRegistration
{
    internal static IServiceCollection AddToolkitOptions(this IServiceCollection services)
    {
        services.AddOptions<ToolkitOptions>()
            .BindConfiguration(ToolkitOptions.SectionName)
            .PostConfigure(options => options.ApplyDefaults())
            .Validate(options => IsDirectoryPath(options.DataRoot),
                "Toolkit:DataRoot must be a valid, non-whitespace directory path.")
            .Validate(options => IsDirectoryPath(options.PackRoot),
                "Toolkit:PackRoot must be a valid, non-whitespace directory path.")
            .Validate(options => IsDirectoryPath(options.PresetRoot),
                "Toolkit:PresetRoot must be a valid, non-whitespace directory path.")
            .ValidateOnStart();
        return services;
    }

    private static bool IsDirectoryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            // Check syntax without requiring a directory to exist or changing relative-path semantics.
            _ = Path.GetFullPath(path);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
