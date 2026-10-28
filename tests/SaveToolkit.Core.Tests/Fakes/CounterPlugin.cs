using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.Fakes;

/// <summary>
/// Minimal reference plugin: "CNTR" magic, int32 counter, uint8 flag, UTF-8 name (16 bytes, zero padded).
/// Doubles as the smallest complete example of the plugin contract.
/// </summary>
public sealed class CounterPlugin : IGamePlugin
{
    public static readonly byte[] Magic = "CNTR"u8.ToArray();

    public PluginInfo Info { get; } = new("test.counter", "Counter test format", "1.0.0")
    {
        Games = [new GameTitle("PPSA99999", "Counter Game")],
    };

    public ProbeResult Probe(ISaveFile file) =>
        file.ReadHeader(4).AsSpan().SequenceEqual(Magic) ? ProbeResult.Match(ProbeScore.Title, "Counter save") : ProbeResult.None;

    public ISaveDocument Open(ISaveFile file)
    {
        var bytes = file.ReadAllBytes();
        if (bytes.Length != 25 || !bytes.AsSpan(0, 4).SequenceEqual(Magic))
        {
            throw new SaveFormatException("Not a counter save.");
        }

        return new CounterDocument(file, bytes);
    }

    public static byte[] Create(int counter, bool flag = false, string name = "hero")
    {
        var bytes = new byte[25];
        Magic.CopyTo(bytes, 0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), counter);
        bytes[8] = flag ? (byte)1 : (byte)0;
        System.Text.Encoding.UTF8.GetBytes(name).CopyTo(bytes, 9);
        return bytes;
    }

    private sealed class CounterDocument(ISaveFile file, byte[] bytes) : BoundSaveDocument
    {
        public override string Title => "Counter";

        public override ViewNode BuildView()
        {
            // Bound but never shown, like values on lazy pages the user has not expanded.
            _ = Field("hidden.flag", "Hidden flag", new BoolSpec(), () => FieldValue.Of(bytes[8] != 0), v => bytes[8] = v.AsBool() ? (byte)1 : (byte)0);
            return VisibleView();
        }

        private SectionNode VisibleView() => new("Counter",
        [
            Field("counter", "Counter", IntegerSpec.Int32 with { Min = 0, Max = 1000 },
                () => FieldValue.Of(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4))),
                v => BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), v.AsInt32()),
                v => v.AsInt32() == 666 ? "Unlucky number." : null),
            Field("flag", "Flag", new BoolSpec(), () => FieldValue.Of(bytes[8] != 0), v => bytes[8] = v.AsBool() ? (byte)1 : (byte)0),
            Field("name", "Name", new TextSpec { MaxLength = 16 },
                () => FieldValue.Of(System.Text.Encoding.UTF8.GetString(bytes.AsSpan(9, 16)).TrimEnd('\0')),
                SetName),
            Field("checksum", "Checksum", IntegerSpec.UInt8, () => FieldValue.Of((long)Checksum())),
            new ActionNode("max", "Max counter") { Parameters = [new FieldNode("value", "Value", IntegerSpec.Int32 with { Min = 0, Max = 1000 }, FieldValue.Of(1000L))] },
            new ActionNode("toggle-hidden", "Toggle hidden flag"),
        ]);

        public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) => actionId switch
        {
            "max" => new ActionOutcome { Changes = SetValues([new ValueEdit("counter", parameters["value"])]), Message = "Counter set." },
            "toggle-hidden" => new ActionOutcome { Changes = SetValues([new ValueEdit("hidden.flag", FieldValue.Of(bytes[8] == 0))]) },
            _ => base.InvokeAction(actionId, parameters),
        };

        public override IReadOnlyList<FileOutput> Serialize() => [new FileOutput(file.RelativePath, bytes.ToArray())];

        protected override IEnumerable<ValueChange> DerivedChanges(IReadOnlyList<ValueChange> applied) => [];

        private void SetName(FieldValue value)
        {
            var encoded = System.Text.Encoding.UTF8.GetBytes(value.AsString());
            if (encoded.Length > 16)
            {
                throw new ArgumentException("Name is longer than 16 bytes in UTF-8.");
            }

            bytes.AsSpan(9, 16).Clear();
            encoded.CopyTo(bytes, 9);
        }

        private byte Checksum()
        {
            byte sum = 0;
            foreach (var b in bytes.AsSpan(4, 5))
            {
                sum += b;
            }

            return sum;
        }
    }
}
