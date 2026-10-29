using System.Collections;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;

namespace SaveToolkit.Core.Tests;

public sealed class OverlayCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancelled_overlay_stops_file_traversal_without_touching_inputs(bool before)
    {
        using var cancellation = new CancellationTokenSource();
        using var store = new WorkspaceStore(new PluginCatalog([]), new() { DataRoot = TestZips.TempDir() });
        var workspace = store.Create("test", [
            new ImportSource("save/a.dat", () => new MemoryStream([1])),
            new ImportSource("save/b.dat", () => new MemoryStream([2]))]);
        var overrides = new CancellingOverrides(cancellation);
        if (before) cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => new OverlaySaveSet(workspace.Set, overrides, cancellation.Token));

        Assert.Equal(before ? 0 : 1, overrides.Lookups);
        Assert.Equal(new byte[] { 1, 2 }, workspace.Set.AllDiskFiles.SelectMany(file => file.ReadAllBytes()));
        Assert.Empty(workspace.Documents);
    }

    private sealed class CancellingOverrides(CancellationTokenSource cancellation) : IReadOnlyDictionary<string, ReadOnlyMemory<byte>>
    {
        public int Lookups { get; private set; }
        public int Count => 0;
        public IEnumerable<string> Keys => [];
        public IEnumerable<ReadOnlyMemory<byte>> Values => [];
        public ReadOnlyMemory<byte> this[string key] => throw new KeyNotFoundException();
        public bool ContainsKey(string key) => false;
        public bool TryGetValue(string key, out ReadOnlyMemory<byte> value)
        {
            Lookups++;
            cancellation.Cancel();
            value = default;
            return false;
        }
        public IEnumerator<KeyValuePair<string, ReadOnlyMemory<byte>>> GetEnumerator() =>
            Enumerable.Empty<KeyValuePair<string, ReadOnlyMemory<byte>>>().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
