using System.Runtime.CompilerServices;
using SaveToolkit.Testing;

namespace SaveToolkit.Tests;

internal static class CultureMatrix
{
    [ModuleInitializer]
    internal static void Initialize() => TestCulture.InitializeFromEnvironment();
}
