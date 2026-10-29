using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

[Collection(typeof(AllocationTestCollection))]
public sealed class SampleFormatTests(ITestOutputHelper output)
{
    private readonly GearboxPlugin _plugin = new();
    // Largest supplied character opens allocate about 20 MB after schema initialization.
    private const long SampleOpenAllocationLimit = 32 * 1024 * 1024;

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void EveryGenerationAndBackup_DecodesAndRoundTrips(string title)
    {
        var files = Files(title).ToArray();
        Assert.Equal(12, files.Length);
        Assert.Equal(3, files.Select(f => f.Directory.Generation).Distinct().Count());
        using (var warmup = _plugin.Open(files[0])) { }
        foreach (var file in files)
        {
            var allocated = AllocationProbe.MeasureOpen(() => _plugin.Open(file), out var opened);
            using var document = opened;
            output.WriteLine($"{file.RelativePath}: open allocated {allocated.MinimumBytes:N0} bytes.");
            allocated.AssertLessThan(SampleOpenAllocationLimit, $"Opening {file.RelativePath} allocated {allocated.MinimumBytes:N0} bytes.");
            Assert.Equal(file.ReadAllBytes(), document.Serialize().Single().Content.ToArray());
            var twin = files.Single(f => f.Directory.Generation == file.Directory.Generation && f.FileName == file.FileName
                && f.Directory.IsBackup != file.Directory.IsBackup);
            Assert.Equal(file.ReadAllBytes(), twin.ReadAllBytes());
        }
    }

    [Theory]
    [InlineData("PPSA01463", 0, 9_520_932, 25_526_805, 4897, 72)]
    [InlineData("PPSA01463", 1, 9_520_932, 13_296_047, 2592, 72)]
    [InlineData("PPSA01463", 2, 6494, 5947, 3, 5)]
    [InlineData("PPSA01493", 0, 70754, 36748, 0, 12)]
    [InlineData("PPSA01493", 1, 75695, 23304, 0, 12)]
    [InlineData("PPSA01493", 2, 70565, 30754, 0, 12)]
    public void CharacterValues_MatchIndependentSampleDecode(string title, int generation, int xp, int money, int secondary, int level)
    {
        var file = Character(title, generation);
        using var document = _plugin.Open(file);
        _ = document.BuildView();
        Assert.Equal(xp, document.GetValue("character/xp").AsInt64());
        Assert.Equal(money, document.GetValue("currency/618814354").AsInt64());
        Assert.Equal(secondary, document.GetValue("currency/3679636065").AsInt64());
        Assert.Equal(level, document.GetValue("character/level").AsInt64());
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void MoneyLevelAndKeys_SurviveReopenForEverySnapshot(string title)
    {
        foreach (var file in Files(title).Where(f => !f.Directory.IsBackup))
        {
            using var document = _plugin.Open(file);
            _ = document.BuildView();
            var profile = file.FileName == "profile";
            ValueEdit[] edits = profile
                ? [new("keys/2420693941", FieldValue.Of(11L))]
                : [new("currency/618814354", FieldValue.Of(123_456_789L)), new("character/level", FieldValue.Of(20L))];
            Assert.True(document.ApplyEdits(edits).Succeeded);
            using var reopened = _plugin.Open(GearboxTestData.Overlay(file, document.Serialize().Single().Content));
            _ = reopened.BuildView();
            foreach (var edit in edits) Assert.Equal(edit.Value, reopened.GetValue(edit.ValueId));
            if (!profile)
            {
                Assert.Equal(20, reopened.GetValue("character/recorded-level").AsInt64());
                var schema = title == "PPSA01493" ? GearboxSchema.Tina : GearboxSchema.Bl3;
                Assert.Equal(schema.Experience[19], reopened.GetValue("character/xp").AsInt64());
            }
        }
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void SessionUndoRedo_RestoresExactXpPointsAndBytes(string title)
    {
        foreach (var file in Files(title).Where(f => !f.Directory.IsBackup))
        {
            using var document = _plugin.Open(file);
            using var session = new DocumentSession("gearbox-test", file, _plugin, document);
            _ = session.BuildView();
            var profile = file.FileName == "profile";
            var id = profile ? "keys/2420693941" : "character/level";
            var value = System.Text.Json.JsonSerializer.SerializeToElement(profile ? 9 : 20);
            var before = document.Serialize().Single().Content.ToArray();
            Assert.True(session.ApplyEdits([new IncomingEdit(id, value)], expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken).Ok);
            var edited = document.Serialize().Single().Content.ToArray();
            Assert.NotEqual(before, edited);
            Assert.True(session.Undo().Ok);
            Assert.Equal(before, document.Serialize().Single().Content.ToArray());
            Assert.True(session.Redo().Ok);
            Assert.Equal(edited, document.Serialize().Single().Content.ToArray());
        }
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void RejectedBatch_IsAtomicIncludingDerivedLevelValues(string title)
    {
        var file = Character(title, 2);
        using var document = _plugin.Open(file);
        _ = document.BuildView();
        var original = document.Serialize().Single().Content.ToArray();
        var outcome = document.ApplyEdits([new("character/level", FieldValue.Of(20L)), new("character/skill-points", FieldValue.Of(-1L))]);
        Assert.False(outcome.Succeeded);
        Assert.Equal(original, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void CorruptedFramingAndPayload_AreRejected(string title)
    {
        var file = Character(title, 0);
        var original = file.ReadAllBytes();
        var container = GearboxContainer.Parse(original, title, false);
        foreach (var bytes in new[] { original[..100], original[..^1], original.ToArray(), original.ToArray() }.Select((b, i) =>
        {
            if (i == 2) b[0] ^= 1;
            if (i == 3) b[container.PayloadOffset] ^= 0xFF;
            return b;
        }))
            Assert.Throws<SaveFormatException>(() => _plugin.Open(GearboxTestData.Overlay(file, bytes)));
    }

    [Fact]
    public void Wonderlands_LevelTwentyAwardsTheExtraSkillPoint()
    {
        var file = Character("PPSA01493", 0);
        using var document = _plugin.Open(file);
        _ = document.BuildView();
        var before = document.GetValue("character/skill-points").AsInt64();
        Assert.True(document.ApplyEdits([new("character/level", FieldValue.Of(20L))]).Succeeded);
        Assert.Equal(before + 9, document.GetValue("character/skill-points").AsInt64());
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void RealSampleEdits_PreserveEveryUnknownWireRecord(string title)
    {
        foreach (var file in Files(title).Where(f => !f.Directory.IsBackup))
        {
            var original = GearboxContainer.Parse(file.ReadAllBytes(), title, file.FileName == "profile").Decode();
            using var document = _plugin.Open(file);
            _ = document.BuildView();
            Assert.True(document.ApplyEdits(file.FileName == "profile"
                ? [new("keys/2420693941", FieldValue.Of(5L))]
                : [new("character/level", FieldValue.Of(20L)), new("currency/618814354", FieldValue.Of(1_000_000_000L))]).Succeeded);
            var edited = GearboxContainer.Parse(document.Serialize().Single().Content.ToArray(), title, file.FileName == "profile").Decode();
            var before = UnknownRecords(original).ToArray();
            var after = UnknownRecords(edited).ToArray();
            Assert.Equal(before.Length, after.Length);
            for (var i = 0; i < before.Length; i++)
                Assert.True(before[i].AsSpan().SequenceEqual(after[i]), "An unknown protobuf record changed during a curated edit.");
        }
    }

    private static IEnumerable<byte[]> UnknownRecords(ProtoMessage message)
    {
        foreach (var field in message.Fields)
        {
            if (field.Spec is null) yield return field.Original;
            if (field.Message is not null)
                foreach (var record in UnknownRecords(field.Message)) yield return record;
        }
    }

    private IEnumerable<DiskSaveFile> Files(string title) => SampleSaves.Load(title).AllDiskFiles.Where(f => _plugin.Probe(f).IsMatch);
    private DiskSaveFile Character(string title, int generation) => Files(title).Single(f => !f.Directory.IsBackup && f.Directory.Generation == generation && f.FileName == "1");
}
