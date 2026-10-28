using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.TalesOfArise.Tests;

public sealed class AriseDecoderHookTests
{
    [Fact]
    public void RegisteredHookDecodesAllSectionWordsAndAlignsBackupTwins()
    {
        var plugin = new TalesOfArisePlugin();
        var hook = Assert.Single(((ISaveDecoderProvider)plugin).Decoders);
        Assert.Equal("tales-of-arise.sections-v1", hook.Id);
        var decoder = new HookSaveDecoder(hook);
        var files = AriseSampleTests.GameplayFiles().ToArray();
        foreach (var primary in files.Where(file => !file.Directory.IsBackup))
        {
            var backup = files.Single(file => file.Directory.IsBackup && file.FileName == primary.FileName);
            Assert.True(hook.CanDecode(primary));
            var tree = decoder.Decode(primary, new DecodeLimits());
            var twin = decoder.Decode(backup, new DecodeLimits());
            Assert.False(tree.Truncated);
            Assert.Equal(138_832, tree.Values.Count);
            Assert.Equal(tree.Values, twin.Values);
            Assert.Equal(tree.Values.Count, tree.Values.Select(value => value.Path).Distinct().Count());
            Assert.All(tree.Values, value => Assert.Equal("integer", value.Kind));
            Assert.Contains(tree.Warnings, warning => warning.Contains("unknown", StringComparison.Ordinal));
            Assert.Equal(primary.ReadAllBytes(), AriseFormat.Encode(AriseFormat.Decode(primary.ReadAllBytes())));
        }
    }

    [Fact]
    public void ControlledEditChangesOnlyItsSectionRelativeWord()
    {
        var file = AriseSampleTests.GameplayFiles().First();
        var decoder = new HookSaveDecoder(Assert.Single(new TalesOfArisePlugin().Decoders));
        var before = decoder.Decode(file, new DecodeLimits());
        using var document = new TalesOfArisePlugin().Open(file);
        _ = document.BuildView();
        Assert.True(document.ApplyEdits([new("party/gald", SaveToolkit.Sdk.Values.FieldValue.Of(12345L))]).Succeeded);
        var edited = MemorySaveSet.SingleFile(file.FileName, Assert.Single(document.Serialize()).Content.ToArray(), "PPSA02745");
        var after = decoder.Decode(edited, new DecodeLimits());
        var changed = Assert.Single(before.Values.Zip(after.Values), pair => pair.First != pair.Second);
        Assert.Equal("evidence/8CC3AF4C/000018", changed.First.Path);
        Assert.Equal(changed.First.Path, changed.Second.Path);
        Assert.Equal("0", changed.First.Value);
        Assert.Equal("12345", changed.Second.Value);
    }

    [Fact]
    public void RelocatingPhysicalSectionsDoesNotChangeEvidencePathsOrValues()
    {
        var file = AriseSampleTests.GameplayFiles().First();
        var save = AriseFormat.Decode(file.ReadAllBytes());
        var first = save.Sections.Single(section => section.Name == "ArisePCStatus_000");
        var second = save.Sections.Single(section => section.Name == "ArisePCStatus_001");
        Assert.Equal(first.Size, second.Size);
        var relocated = save.Payload.ToArray();
        var targets = save.Sections.ToDictionary(section => section.Id, section =>
            section.Id == first.Id ? second.Offset : section.Id == second.Id ? first.Offset : section.Offset);
        for (var index = 0; index < save.Sections.Count; index++)
        {
            var section = save.Sections[index];
            var header = targets[section.Id] - AriseFormat.HeaderSize;
            save.Payload.AsSpan(section.Offset - AriseFormat.HeaderSize, section.Size + AriseFormat.HeaderSize)
                .CopyTo(relocated.AsSpan(header));
            var next = index + 1 < save.Sections.Count ? targets[save.Sections[index + 1].Id] - AriseFormat.HeaderSize : 0;
            BinaryPrimitives.WriteUInt32LittleEndian(relocated.AsSpan(header + 12), (uint)next);
        }

        var bytes = AriseFormat.Encode(save with { Payload = relocated });
        Assert.NotEqual(file.ReadAllBytes(), bytes);
        Assert.Equal(second.Offset, AriseFormat.Decode(bytes).Sections.Single(section => section.Id == first.Id).Offset);
        var decoder = new HookSaveDecoder(Assert.Single(new TalesOfArisePlugin().Decoders));
        var before = decoder.Decode(file, new DecodeLimits());
        var after = decoder.Decode(MemorySaveSet.SingleFile(file.FileName, bytes, "PPSA02745"), new DecodeLimits());
        Assert.False(after.Truncated);
        Assert.Equal(before.Values, after.Values);
    }

    [Fact]
    public void LimitsAreExplicitAndTheHookStopsAtTheFirstRejectedValue()
    {
        var hook = Assert.Single(new TalesOfArisePlugin().Decoders);
        var file = AriseSampleTests.GameplayFiles().First();
        var sink = new RejectingSink();
        hook.Decode(file, sink);
        Assert.Equal(1, sink.Calls);
        Assert.True(sink.Truncated);

        var decoder = new HookSaveDecoder(hook);
        var capped = decoder.Decode(file, new DecodeLimits { MaxValues = 3 });
        Assert.True(capped.Truncated);
        Assert.Equal(3, capped.Values.Count);
        Assert.NotEmpty(capped.Warnings);
        var shallow = decoder.Decode(file, new DecodeLimits { MaxDepth = 2 });
        Assert.True(shallow.Truncated);
        Assert.Empty(shallow.Values);
        Assert.Contains(shallow.Warnings, warning => warning.Contains("depth", StringComparison.Ordinal));
        Assert.Throws<SaveFormatException>(() => hook.Decode(file, new RejectingSink { MaxFileBytes = 1 }));
    }

    [Fact]
    public void ForeignFilesAreNotClaimedAndCorruptCiphertextIsRejected()
    {
        var hook = Assert.Single(new TalesOfArisePlugin().Decoders);
        Assert.False(hook.CanDecode(MemorySaveSet.SingleFile("SaveData_000.sav", new byte[16], "PPSA02745")));
        var file = AriseSampleTests.GameplayFiles().First();
        Assert.False(hook.CanDecode(MemorySaveSet.SingleFile(file.FileName, file.ReadAllBytes(), "PPSA99999")));
        var corrupt = file.ReadAllBytes();
        corrupt[^1] ^= 1;
        Assert.Throws<SaveFormatException>(() => new HookSaveDecoder(hook).Decode(
            MemorySaveSet.SingleFile(file.FileName, corrupt, "PPSA02745"), new DecodeLimits()));
    }

    private sealed class RejectingSink : ISaveDecodeSink
    {
        public int MaxValues => 0;
        public int MaxValueLength => 256;
        public int MaxDepth => 64;
        public long MaxFileBytes { get; init; } = AriseFormat.FileSize;
        public int Calls { get; private set; }
        public bool Truncated { get; private set; }
        public bool Add(string path, string kind, string value, string? label = null)
        {
            Calls++;
            return false;
        }

        public void Warn(string message) { }
        public void MarkTruncated(string reason) => Truncated = true;
    }
}
