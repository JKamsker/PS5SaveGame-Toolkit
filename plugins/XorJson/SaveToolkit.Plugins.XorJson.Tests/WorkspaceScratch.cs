using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.XorJson.Tests;

/// <summary>Declare before the store so its imported files are closed before cleanup runs.</summary>
internal sealed class WorkspaceScratch : IDisposable
{
    public string Root { get; } = Path.Combine(TestScratch.ProcessFolder, "xorjson-" + Guid.NewGuid().ToString("N"));

    public WorkspaceScratch() => Directory.CreateDirectory(Root);

    public void Dispose()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}
