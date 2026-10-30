using System.Text.Json;
using SaveToolkit.Plugins.Gearbox.Tests;

namespace Gearbox.BrowserFixture;

internal static class Program
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static void Main() => Console.WriteLine("GEARBOX_FIXTURE:" + JsonSerializer.Serialize(
        new[] { GearboxBrowserFixture.Create(false), GearboxBrowserFixture.Create(true) },
        Options));
}
