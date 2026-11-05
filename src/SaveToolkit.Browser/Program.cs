using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("browser")]

namespace SaveToolkit.Browser;

/// <summary>
/// The runtime is driven entirely through <see cref="Interop"/> exports from the web worker;
/// Main only has to exist so the WebAssembly SDK emits a runnable app.
/// </summary>
public static class Program
{
    public static void Main()
    {
    }
}
