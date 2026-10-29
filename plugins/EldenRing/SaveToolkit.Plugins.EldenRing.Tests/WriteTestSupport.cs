using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.EldenRing.Tests;

internal static class WriteTestSupport
{
    public static DiskSaveFile Sample() => SampleSaves.Load("PPSA03169").AllDiskFiles
        .First(file => file.FileName == "memory.dat" && !file.Directory.IsBackup && file.Directory.Generation == 0);

    public static DocumentSession Session(DiskSaveFile file, out ISaveDocument document, byte[]? initialBytes = null)
    {
        var plugin = new EldenRingPlugin();
        var input = initialBytes is null ? (ISaveFile)file : File(initialBytes);
        document = plugin.Open(input);
        var session = new DocumentSession("enabled-writes", file, plugin, document, openOriginal: () => plugin.Open(input));
        _ = session.BuildView();
        return session;
    }

    public static byte[] Bytes(ISaveDocument document) => Assert.Single(document.Serialize()).Content.ToArray();
    public static IncomingEdit Incoming<T>(string id, T value) => new(id, JsonSerializer.SerializeToElement(value));
    public static void EqualBytes(byte[] expected, ISaveDocument document) => Assert.True(expected.AsSpan().SequenceEqual(Bytes(document)));
    public static ISaveFile File(byte[] bytes) => MemorySaveSet.SingleFile("memory.dat", bytes, "PPSA03169", "sce_sdmemory");
    public static ISaveDocument Reopen(ISaveDocument document) => new EldenRingPlugin().Open(File(Bytes(document)));

    public static void OnlyRanges(byte[] before, byte[] after, params (int Offset, int Length)[] allowed)
    {
        var diff = ByteDiffer.Compare(before, after, options: new() { MergeGap = 0, ContextBytes = 0, MaxPreviewBytes = 1 });
        Assert.False(diff.Truncated);
        Assert.Equal(before.Length, after.Length);
        Assert.All(diff.Ranges, change => Assert.Contains(allowed,
            range => change.Offset >= range.Offset && change.End <= range.Offset + range.Length));
    }

    public static FieldNode Quantity(DocumentSession session, string container, string rawId) =>
        Assert.IsType<FieldNode>(Assert.Single(session.QueryCollection("slots/0/inventory/" + container,
            new() { Text = rawId }).Rows).Cells["quantity"]);
}
