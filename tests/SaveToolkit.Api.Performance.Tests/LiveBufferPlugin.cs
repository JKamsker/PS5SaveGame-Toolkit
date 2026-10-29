using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Api.Performance.Tests;

/// <summary>Deliberately returns live plugin memory to exercise the host's snapshot boundary.</summary>
internal sealed class LiveBufferPlugin(byte[] bytes) : IGamePlugin
{
    public byte[] Bytes => bytes;
    public PluginInfo Info { get; } = new("test.live-buffer", "Live buffer", "1.0.0");
    public ProbeResult Probe(ISaveFile file) => ProbeResult.Match(ProbeScore.Format);
    public ISaveDocument Open(ISaveFile file) => new Document(file.RelativePath, bytes);

    private sealed class Document(string path, byte[] bytes) : BoundSaveDocument
    {
        public override string Title => "Live buffer";
        public override ViewNode BuildView() => new SectionNode("Live buffer",
        [
            Field("value", "Value", IntegerSpec.UInt8, () => FieldValue.Of(bytes[0]), value => bytes[0] = (byte)value.AsInt32()),
            new ImageNode("image", "Live image"),
            new HexNode("blob", bytes.Length),
        ]);
        public override IReadOnlyList<FileOutput> Serialize() => [new(path, bytes)];
        public override AssetContent? GetAsset(string assetId) => new("image/png", bytes);
        public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count) => bytes.AsMemory((int)offset, count);
    }
}
