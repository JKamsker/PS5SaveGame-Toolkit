using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

/// <summary>Three independent wallets with the same resource ID; expectations use authored physical offsets.</summary>
internal sealed class RagnarokRestoreFixture : IDisposable
{
    private const int ResourceRowsOffsetInWallet = 20;
    private const string ImportedPath = "PPSA08330/sdimg_generated/GOWRSAVE0";
    public const int WalletCount = 3;
    public const uint ImportedQuantity = 1234;
    public const string CollectionId = "resources";
    public const string ActionId = "resources/restore-imported";
    public const string TimeId = "metadata/play-seconds";
    public static IReadOnlyDictionary<string, JsonElement> Parameters => new Dictionary<string, JsonElement>();
    public RagnarokDocument Document { get; }
    public DocumentSession Session { get; }
    public ViewNode Root { get; }
    public byte[] Original { get; } = Bytes();

    public RagnarokRestoreFixture()
    {
        var file = ImportFile(Original);
        var plugin = new GodOfWarRagnarokPlugin();
        Document = (RagnarokDocument)plugin.Open(file);
        Session = new("restore-selection", file, plugin, Document);
        Root = Session.BuildView();
        var page = Session.QueryCollection(CollectionId, new() { Limit = WalletCount }, expectedRevision: null,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(WalletCount, page.Rows.Count);
        for (var wallet = 0; wallet < WalletCount; wallet++)
        {
            Assert.Equal(RowKey(wallet), page.Rows[wallet].Key);
            Assert.Equal(ValueId(wallet), Assert.IsType<FieldNode>(page.Rows[wallet].Cells["quantity"]).ValueId);
        }
        Assert.Equal(Original, Serialized());
    }

    public static byte[] Bytes()
    {
        var bytes = new RagnarokNestedFixture().Create();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(RagnarokFormat.PlaySecondsOffset), 10);
        return bytes;
    }

    public static ISaveFile File() => RagnarokTestSupport.GameplayFile(Bytes());

    public static DiskSaveFile ImportFile(byte[] bytes)
    {
        var storage = Path.Combine(TestScratch.ProcessFolder, nameof(RagnarokRestoreFixture), Guid.NewGuid().ToString("N"));
        var set = SaveSetImporter.Import([new ImportSource(ImportedPath, () => new MemoryStream(bytes))],
            storage, limits: null, cancellationToken: TestContext.Current.CancellationToken);
        return Assert.Single(set.AllDiskFiles);
    }

    public static string RowKey(int wallet)
    {
        var container = wallet == 0 ? RagnarokFormat.HeroWalletId : (ulong)wallet;
        return $"wallet/{container:X16}/resource/5AE80674D240529E/0";
    }

    public static string ValueId(int wallet) => RowKey(wallet) + "/quantity";

    public void Reduce()
    {
        var edits = Enumerable.Range(0, WalletCount).Select(wallet => Incoming(ValueId(wallet), wallet + 1))
            .Append(Incoming(TimeId, 9)).ToArray();
        Assert.True(Session.ApplyEdits(edits, expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.Equal(Expected(1, 2, 3), Serialized());
    }

    public byte[] Expected(params uint[] quantities)
    {
        Assert.Equal(WalletCount, quantities.Length);
        var expected = Original.ToArray();
        var framing = new RagnarokNestedFixture();
        for (var wallet = 0; wallet < WalletCount; wallet++)
        {
            var offset = framing.WalletOffset(wallet) + ResourceRowsOffsetInWallet + sizeof(ulong);
            BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(offset), quantities[wallet]);
        }
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(RagnarokFormat.PlaySecondsOffset), 9);
        return expected;
    }

    public byte[] Serialized() => Assert.Single(Document.Serialize()).Content.ToArray();
    public static IncomingEdit Incoming(string id, int value) =>
        new(id, JsonSerializer.SerializeToElement(value.ToString(CultureInfo.InvariantCulture)));
    public void Dispose() => Session.Dispose();
}
