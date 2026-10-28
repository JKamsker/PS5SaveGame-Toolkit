using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class DiagnosticRobustnessTests
{
    [Fact]
    public void LongOpaqueTextHasABoundedPreviewAndExportBuffersCannotMutateTheDocument()
    {
        const int sectionStart = 32;
        const int payloadSize = 4096;
        var bytes = new byte[sectionStart + payloadSize];
        bytes.AsSpan(sectionStart).Fill((byte)'Z');
        Write(0, 0x44415431); Write(4, 8); Write(8, (uint)bytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), 1);
        Write(16, 0x12345678); Write(20, sectionStart); Write(24, payloadSize);
        using var document = new InsomniacPlugin().Open(MemorySaveSet.SingleFile("test.save", bytes));
        _ = document.BuildView();
        var section = Assert.Single(document.ExpandNode("section/0").OfType<SectionNode>());
        var preview = Assert.IsType<TextNode>(Assert.Single(section.Children)).Text;
        Assert.EndsWith("…", preview);
        Assert.True(preview.Length < 300);
        Assert.Equal(bytes.AsSpan(sectionStart).ToArray(), document.ReadBlob("section/0/bytes", 0, payloadSize).ToArray());
        var output = Assert.Single(document.Serialize()).Content;
        Assert.True(MemoryMarshal.TryGetArray(output, out var segment));
        segment.Array![segment.Offset] ^= 1;
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        void Write(int offset, uint data) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), data);
    }

    [Theory]
    [InlineData("CareerBolts")]
    [InlineData("LevelGoldBolts")]
    [InlineData("RYNOPlansCollected")]
    [InlineData("VanityPickupsCollected")]
    public void NegativeCountersAndMasksFailWithAFormatMessageInsteadOfOverflowingTheView(string name)
    {
        var file = PlayerCorpusTests.Find("i29slot0-s-autosave", 0);
        var bytes = file.ReadAllBytes();
        var container = Dat1Container.Parse(bytes);
        var section = Assert.Single(container.Sections, s => s.Tag == RiftPlayerView.CampaignTag);
        var fields = new DdlReader(container, section).Read();
        var field = fields[name];
        var (kind, width) = field.Kind switch
        {
            DdlValueKind.UInt8 => (DdlValueKind.Int8, 1),
            DdlValueKind.UInt16 => (DdlValueKind.Int16, 2),
            DdlValueKind.UInt32 => (DdlValueKind.Int32, 4),
            DdlValueKind.UInt64 => (DdlValueKind.Int64, 8),
            _ => throw new InvalidOperationException("Unexpected corpus width."),
        };
        var start = checked((int)section.Offset);
        var count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(start + 8));
        var hash = DdlReader.NameHash(Encoding.UTF8.GetBytes(name));
        for (var index = 0; index < count; index++)
        {
            var descriptor = start + 16 + index * 8;
            if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(descriptor)) != hash) continue;
            var meta = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(descriptor + 4));
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(descriptor + 4), (meta & 0xFFFFFF) | ((uint)kind << 24));
            break;
        }
        bytes.AsSpan(field.Offset, width).Fill(byte.MaxValue);
        var exception = Assert.Throws<SaveFormatException>(() => new InsomniacPlugin().Open(MemorySaveSet.SingleFile(file.FileName, bytes, "PPSA01474")));
        Assert.Contains(name, exception.Message);
    }

    [Fact]
    public void OptionalDiagnosticShapesStayUnresolvedWithoutLinqCardinalityErrors()
    {
        Assert.Null(DdlShape.Text(null));
        Assert.Null(DdlShape.Fields(null));
        Assert.Null(DdlShape.Text(new("CodeName", DdlValueKind.String, 0, 0, [])));
        Assert.Null(DdlShape.Text(new("CodeName", DdlValueKind.UInt32, 1, 0, [FieldValue.Of(1L)])));
        var field = new DdlField("Obj", DdlValueKind.Object, 2, 0, [])
        {
            Objects = [new(0, 0, new Dictionary<string, DdlField>()), new(0, 0, new Dictionary<string, DdlField>())],
        };
        Assert.Null(DdlShape.Fields(field));
        Assert.Equal("Known", DdlShape.Text(new("CodeName", DdlValueKind.String, 1, 0, [FieldValue.Of("Known")])));
    }

    [Theory]
    [InlineData(9007199254740993UL)]
    [InlineData(ulong.MaxValue)]
    public void DdlUnsigned64NumbersRemainExactThroughJsonAndNoOpSerialization(ulong value)
    {
        const int sectionStart = 64;
        var bytes = new byte[100];
        Write(0, 0x44415431); Write(4, 8); Write(8, (uint)bytes.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), 1);
        Write(16, 0x12345678); Write(20, sectionStart); Write(24, 36);
        Encoding.UTF8.GetBytes("Value\0").CopyTo(bytes, 28);
        Write(sectionStart + 4, 0x03150044); Write(sectionStart + 8, 1); Write(sectionStart + 12, 20);
        Write(sectionStart + 16, DdlReader.NameHash("Value"u8));
        Write(sectionStart + 20, ((uint)DdlValueKind.UInt64 << 24) | 0x10); Write(sectionStart + 24, 28);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(sectionStart + 28), value);
        var field = NestedDdlTests.Read(bytes)["Value"];
        Assert.Equal(value, field.Values[0].AsUInt64());
        var json = JsonSerializer.SerializeToElement(field.Values[0]);
        Assert.Equal(JsonValueKind.String, json.ValueKind);
        var coerced = FieldValueCoercer.FromJson(json, field.Spec);
        Assert.True(coerced.Ok);
        Assert.Equal(field.Values[0], coerced.Value);
        using var document = new InsomniacPlugin().Open(MemorySaveSet.SingleFile("test.save", bytes));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        void Write(int offset, uint data) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), data);
    }
}
