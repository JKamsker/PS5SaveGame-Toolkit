using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using SaveToolkit.Core.Plugins;

[assembly: SupportedOSPlatform("browser")]

namespace SaveToolkit.Browser;

public static partial class Program
{
    public static void Main() { }

    [JSExport]
    public static string Plugins(string names)
    {
        foreach (var n in names.Split(',', StringSplitOptions.RemoveEmptyEntries)) Assembly.Load(new AssemblyName(n));
        return string.Join(",", PluginCatalog.Discover().Plugins.Select(p => p.Info.Id));
    }
}
