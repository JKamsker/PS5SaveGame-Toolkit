using System.Text.Json;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Research;
using SaveToolkit.Testing;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class DifferentialEvidenceTests
{
    private static readonly DecodeLimits Full = new() { MaxValues = 1_000_000, MaxDepth = 100 };

    [Fact]
    public void ArchiveHasOnlyOneGenerationAndF1DoesNotInventBeforeAfterEvidence()
    {
        var set = SampleSaves.Load("PPSA02530");
        Assert.All(set.Directories, directory => Assert.Equal(0, directory.Generation));
        var report = SaveSetDiffer.Compare(set, set, new SaveDiffOptions
        {
            BeforeGeneration = GenerationSelector.Only(0), AfterGeneration = GenerationSelector.Latest,
            Decoder = new PragmataDecoder(),
        });
        Assert.Equal(12, report.Totals.Unchanged);
        Assert.False(report.HasChanges);
    }

    [Fact]
    public void EverySampleDecodesCompletelyWithUniqueCanonicalPaths()
    {
        var decoder = new PragmataDecoder();
        foreach (var file in SampleEvidenceTests.Files())
        {
            var tree = decoder.Decode(file, Full);
            Assert.False(tree.Truncated);
            Assert.NotEmpty(tree.Values);
            Assert.Equal(tree.Values.Count, tree.Values.Select(v => v.Path).Distinct(StringComparer.Ordinal).Count());
            Assert.DoesNotContain(tree.Warnings, w => w.StartsWith("Duplicate identity", StringComparison.Ordinal));
            Assert.All(tree.Values.Where(v => v.Kind is "string" or "struct"), v => Assert.Contains("sha256=", v.Value, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void SectionAndInventoryReorderingDoesNotCreateSemanticChanges()
    {
        var file = SampleEvidenceTests.Files().Single(f => f.FileName == "data000.bin");
        var bytes = new DsssContainer(file.ReadAllBytes()).Plaintext;
        var layout = new SaveLayout(bytes);
        var decoder = new PragmataDecoder();
        var before = decoder.Decode(bytes, layout.Roots, Full);
        layout.Inventory!.Find(KnownHashes.Items)!.Children = layout.Items.Reverse().ToArray();
        layout.Roots.Single(n => n.Hash == KnownHashes.GameRoot).Find(KnownHashes.GameSections)!.Children = layout.Sections.Reverse().ToArray();
        var after = decoder.Decode(bytes, layout.Roots, Full);
        Assert.Equal(0, SemanticDiffer.Compare(before, after).Total);
        Assert.Contains(after.Values, v => v.Path.Contains("[key=1b5a30c0:2227368435]", StringComparison.Ordinal));
    }

    [Fact]
    public void QuantityEditReportsOneCanonicalChangeAndOnlyItsPlaintextBytes()
    {
        using var document = EditingTests.Gameplay();
        var file = SampleEvidenceTests.Files().Single(f => f.FileName == "data000.bin");
        var beforeBytes = new DsssContainer(file.ReadAllBytes()).Plaintext;
        var quantity = EditingTests.Quantity(document);
        Assert.True(document.ApplyEdits([new(quantity.Path, Sdk.Values.FieldValue.Of(2576L))]).Succeeded);
        var afterBytes = new DsssContainer(document.Serialize().Single().Content.Span).Plaintext;
        var decoder = new PragmataDecoder();
        var changes = SemanticDiffer.Compare(decoder.Decode(beforeBytes, new SaveLayout(beforeBytes).Roots, Full),
            decoder.Decode(afterBytes, new SaveLayout(afterBytes).Roots, Full));
        var change = Assert.Single(changes.Changes);
        Assert.Equal("root[7c416083]/7c018857[class=3a83076c]/3185b7a5[key=1b5a30c0:2227368435]/d18b252d", change.Path);
        Assert.Equal("2575", change.Before);
        Assert.Equal("2576", change.After);
        var bytes = ByteDiffer.Compare(beforeBytes, afterBytes, options: new ByteDiffOptions { MergeGap = 0 });
        Assert.All(bytes.Ranges, range => Assert.InRange(range.Offset, quantity.Offset, quantity.Offset + quantity.Size - 1));
        Assert.InRange(bytes.ChangedBytes, 1, quantity.Size);
    }

    [Fact]
    public void DuplicateRecordKeysFallBackToPositionsWithAnExplicitWarning()
    {
        var bytes = new byte[8];
        var array = new SaveNode(1, FieldTags.Array);
        array.Children = Enumerable.Range(0, 2).Select(i =>
        {
            var row = new SaveNode(0, FieldTags.Class, array, i) { ClassHash = 42 };
            row.Children = [new SaveNode(KnownHashes.ItemId, FieldTags.UInt32, row) { Offset = i * 4, Size = 4 }];
            return row;
        }).ToArray();
        var tree = new PragmataDecoder().Decode(bytes, [array], Full);
        Assert.Contains(tree.Warnings, w => w.Contains("positional fallback", StringComparison.Ordinal));
        Assert.Contains(tree.Values, v => v.Path.Contains("[index=1]", StringComparison.Ordinal));
        Assert.DoesNotContain(tree.Warnings, w => w.StartsWith("Duplicate identity", StringComparison.Ordinal));
    }

    [Fact]
    public void DecoderBoundsAreVisibleAndCannotProveRemoval()
    {
        var file = SampleEvidenceTests.Files().First();
        var decoder = new PragmataDecoder();
        var shortTree = decoder.Decode(file, Full with { MaxValues = 3 });
        Assert.True(shortTree.Truncated);
        Assert.Equal(3, shortTree.Values.Count);
        Assert.True(decoder.Decode(file, Full with { MaxDepth = 0 }).Truncated);
        Assert.True(decoder.Decode(file, Full with { MaxFileBytes = 1 }).Truncated);
        Assert.True(SemanticDiffer.Compare(decoder.Decode(file, Full), shortTree).InputTruncated);
    }

    [Fact]
    public void OpaqueGuidKeysRemainStableAcrossReorderingWithoutExposingBytes()
    {
        var bytes = new byte[32];
        bytes[0] = 1;
        bytes[16] = 2;
        var array = new SaveNode(1, FieldTags.Array);
        array.Children = Enumerable.Range(0, 2).Select(i =>
        {
            var row = new SaveNode(0, FieldTags.Class, array, i) { ClassHash = 42 };
            row.Children = [new SaveNode(KnownHashes.ItemId, FieldTags.Struct, row) { Offset = i * 16, Size = 16 }];
            return row;
        }).ToArray();
        var decoder = new PragmataDecoder();
        var before = decoder.Decode(bytes, [array], Full);
        array.Children = array.Children.Reverse().ToArray();
        var after = decoder.Decode(bytes, [array], Full);
        Assert.Equal(0, SemanticDiffer.Compare(before, after).Total);
        Assert.Contains(before.Values, v => v.Path.Contains("opaque-sha256:", StringComparison.Ordinal));
        Assert.Empty(before.Warnings);
    }

    [Fact]
    public void FullStringFingerprintDetectsChangesBeyondPreviewWithoutPrintingText()
    {
        var bytes = System.Text.Encoding.Unicode.GetBytes(new string('X', 200));
        var root = new SaveNode(1, FieldTags.String) { Offset = 0, Size = bytes.Length };
        var decoder = new PragmataDecoder();
        var before = decoder.Decode(bytes, [root], Full with { MaxValueLength = 16 });
        bytes[^1] ^= 1;
        var after = decoder.Decode(bytes, [root], Full with { MaxValueLength = 16 });
        Assert.Single(SemanticDiffer.Compare(before, after).Changes);
        Assert.DoesNotContain("XXXX", Assert.Single(before.Values).Value, StringComparison.Ordinal);
    }

    [Fact]
    public void ManifestPinsSamplesAndIndexesAllTwentySevenSectionsWithoutClaimingMeaning()
    {
        using var stream = typeof(DifferentialEvidenceTests).Assembly.GetManifestResourceStream("Pragmata.Evidence.json")!;
        using var json = JsonDocument.Parse(stream);
        var root = json.RootElement;
        Assert.Equal(SchemaCatalog.Sha256, root.GetProperty("schemaSha256").GetString());
        var samples = root.GetProperty("samples").EnumerateArray().ToArray();
        Assert.Equal(12, samples.Length);
        var files = SampleEvidenceTests.Files().ToDictionary(f => f.FileName);
        foreach (var sample in samples)
            Assert.Equal(((Core.Storage.DiskSaveFile)files[sample.GetProperty("file").GetString()!]).Sha256, sample.GetProperty("sha256").GetString());
        var sections = root.GetProperty("sections").EnumerateArray().ToArray();
        Assert.Equal(27, sections.Length);
        Assert.All(sections, section =>
        {
            Assert.Equal("Verified", section.GetProperty("nameConfidence").GetString());
            Assert.Equal(11, section.GetProperty("samples").GetInt32());
        });
        Assert.All(root.GetProperty("identifiers").EnumerateArray(), id => Assert.Equal("Unknown", id.GetProperty("semanticConfidence").GetString()));
        foreach (var family in new[] { "Inventory", "Weapon", "Enhance", "EquipmentPreset", "Objective", "GameOccurrenceEvent", "GameOccurrenceRecord", "CheckPoint", "Chapter" })
            Assert.Contains(root.GetProperty("identifiers").EnumerateArray(), id => id.GetProperty("family").GetString() == $"app.{family}Manager.SaveData");
    }
}
