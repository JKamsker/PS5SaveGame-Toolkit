using System.Reflection;

namespace SaveToolkit.Browser;

/// <summary>Loads the build-time plugin manifest so plugin discovery can see every assembly.</summary>
internal static class PluginLoader
{
    /// <summary>
    /// Loads each plugin assembly by name. A plugin that cannot be loaded is reported and skipped,
    /// so one broken assembly never takes the whole application down.
    /// </summary>
    public static IReadOnlyList<string> LoadAll(IEnumerable<string> assemblyNames, Action<string> log)
    {
        var failures = new List<string>();
        foreach (var name in assemblyNames)
        {
            try
            {
                Assembly.Load(new AssemblyName(name));
            }
            catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                failures.Add(name);
                log($"Plugin assembly '{name}' could not be loaded and was skipped: {exception.Message}");
            }
        }

        return failures;
    }
}
