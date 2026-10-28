using SaveToolkit.Testing.Diff;
using static SaveToolkit.SaveDiff.Tests.TestSaves;

namespace SaveToolkit.SaveDiff.Tests;

public sealed class FixtureManifestTests
{
    private const string ValidManifest = """
        {
          "format": "savetoolkit-fixtures/1",
          // comments and trailing commas are allowed
          "ignore": [ { "titleId": "PPSA99999", "file": "data.bin", "offset": 0, "length": 4, "reason": "save counter" } ],
          "fixtures": [
            {
              "id": "ppsa99999-buy-potion",
              "titleId": "PPSA99999",
              "game": "Synthetic",
              "build": "01.000.000",
              "region": "EU",
              "slot": "slot 1",
              "dlc": [],
              "event": "Bought one potion",
              "before": { "archive": "before.zip", "generation": "latest" },
              "after": { "archive": "after.zip", "generation": 0, "sha256": "00" },
              "decoder": "json",
              "expected": [
                { "file": "data.bin", "offset": 8, "length": 2, "before": "0000", "after": "0102" },
                { "file": "*.json", "path": "potions", "before": "1", "after": "2", "note": "count" },
              ],
            },
          ],
        }
        """;

    [Fact]
    public void Parse_ReadsAllProvenanceFields()
    {
        var manifest = FixtureManifest.Parse(ValidManifest);

        var fixture = Assert.Single(manifest.Fixtures);
        Assert.Equal(("PPSA99999", "01.000.000", "PS5", "slot 1"), (fixture.TitleId, fixture.Build, fixture.Platform, fixture.Slot));
        Assert.Equal(GenerationSelector.Latest, fixture.Before!.Generation);
        Assert.Equal(GenerationSelector.Only(0), fixture.After!.Generation);
        Assert.Equal(2, fixture.Expected.Count);
        Assert.Equal("save counter", Assert.Single(manifest.Ignore).Reason);
    }

    [Theory]
    [InlineData("""{"format":"other/1"}""", "unsupported format")]
    [InlineData("""{"fixtures":[{"id":"a","titleId":"bad","event":"x","before":{"archive":"a"},"after":{"archive":"b"}}]}""", "titleId")]
    [InlineData("""{"fixtures":[{"id":"a","titleId":"PPSA00001","before":{"archive":"a"},"after":{"archive":"b"}}]}""", "'event' is required")]
    [InlineData("""{"fixtures":[{"id":"a","titleId":"PPSA00001","event":"x","before":{"archive":"a"}}]}""", "after.archive")]
    [InlineData("""{"ignore":[{"file":"*"}]}""", "reason")]
    [InlineData("""{"fixtures":[{"id":"a","titleId":"PPSA00001","event":"x","before":{"archive":"a"},"after":{"archive":"b"},"expected":[{"offset":1,"length":2,"after":"zz"}]}]}""", "hex")]
    [InlineData("""{"fixtures":[{"id":"a","titleId":"PPSA00001","event":"x","before":{"archive":"a"},"after":{"archive":"b"},"expected":[{"file":"x"}]}]}""", "either 'path'")]
    [InlineData("""{"unknownField":1}""", "invalid JSON")]
    public void Parse_RejectsInvalidManifests(string json, string expectedError)
    {
        var ex = Assert.Throws<FixtureManifestException>(() => FixtureManifest.Parse(json));

        Assert.Contains(expectedError, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Verify_ChecksByteAndSemanticExpectations()
    {
        var data = Bytes(16);
        using var before = Load((Primary("data.bin"), With(data, 0, 9)), (Primary("p.json"), Utf8("""{"potions":1,"gold":50}""")));
        using var after = Load((Primary("data.bin"), With(data, 0, 8, 0, 0, 0, 0, 0, 0, 0, 1, 2)), (Primary("p.json"), Utf8("""{"potions":2,"gold":0}""")));
        var manifest = FixtureManifest.Parse(ValidManifest);
        var fixture = manifest.Fixtures[0] with { Before = new SaveRef { Archive = "before.zip" }, After = new SaveRef { Archive = "after.zip" } };

        var verification = FixtureVerifier.Verify(fixture, before.Set, after.Set, FixtureVerifier.OptionsFor(fixture, manifest, new JsonSaveDecoder()));

        Assert.True(verification.Passed, TextReportWriter.Render(verification));
        Assert.All(verification.Expectations, e => Assert.True(e.Passed, e.Message));
        // The manifest-level churn rule masked the counter at offset 0.
        Assert.Equal(1, verification.Report.Files.Single(f => f.Path == "data.bin").Bytes!.IgnoredBytes);
    }

    [Fact]
    public void Verify_FailsWhenAValueDiffers_OrAnUnchangedExpectationChanged()
    {
        using var before = Load((Primary("p.json"), Utf8("""{"potions":1,"gold":50}""")));
        using var after = Load((Primary("p.json"), Utf8("""{"potions":3,"gold":0}""")));
        var fixture = Fixture([
            new ExpectedChange { Path = "potions", After = "2" },
            new ExpectedChange { Path = "gold", Changed = false },
            new ExpectedChange { Path = "missing", Before = ExpectedChange.Absent, After = ExpectedChange.Absent, Changed = false },
            new ExpectedChange { File = "nope.bin", Offset = 0, Length = 1 },
        ]);

        var verification = Verify(fixture, before, after, new JsonSaveDecoder());

        Assert.False(verification.Passed);
        Assert.Equal([false, false, true, false], verification.Expectations.Select(e => e.Passed));
        Assert.Contains("after is 3, expected 2", verification.Expectations[0].Message, StringComparison.Ordinal);
        Assert.Contains("expected unchanged", verification.Expectations[1].Message, StringComparison.Ordinal);
        Assert.Contains("no compared file matches", verification.Expectations[3].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StrictMode_ReportsChangesNoExpectationCovers()
    {
        var data = Bytes(64);
        using var before = Load((Primary("data.bin"), data), (Primary("other.bin"), data));
        using var after = Load((Primary("data.bin"), With(With(data, 8, 1), 40, 1)), (Primary("other.bin"), With(data, 0, 1)));
        var fixture = Fixture([new ExpectedChange { File = "data.bin", Offset = 8, Length = 1 }]) with { Strict = true };

        var verification = Verify(fixture, before, after, decoder: null);

        Assert.True(verification.Expectations[0].Passed);
        Assert.Contains(verification.Unexpected, u => u.Contains("0x28", StringComparison.Ordinal));
        Assert.Contains(verification.Unexpected, u => u.Contains("other.bin", StringComparison.Ordinal));
        Assert.False(verification.Passed);
    }

    [Fact]
    public void StrictMode_FlagsAChangeRightNextToAnExpectedOne()
    {
        // A counter at 8 is expected; a "checksum" byte at 10 is within the default merge gap but not expected.
        var data = Bytes(32);
        using var before = Load((Primary("data.bin"), data));
        using var after = Load((Primary("data.bin"), With(data, 8, 1, 0, 7)));
        var fixture = Fixture([new ExpectedChange { File = "data.bin", Offset = 8, Length = 1 }]) with { Strict = true };

        var verification = Verify(fixture, before, after, decoder: null);

        Assert.Contains(verification.Unexpected, u => u.Contains("0xA+1", StringComparison.Ordinal));
        Assert.False(verification.Passed);
    }

    [Fact]
    public void StrictMode_RequiresADecodableFile_ForPathExpectations()
    {
        using var before = Load((Primary("p.json"), Utf8("""{"a":1}""")));
        using var after = Load((Primary("p.json"), Utf8("""{"a":2""")));
        var fixture = Fixture([new ExpectedChange { Path = "a" }]) with { Strict = true };

        var verification = Verify(fixture, before, after, new JsonSaveDecoder());

        Assert.Contains(verification.Unexpected, u => u.Contains("decode failed", StringComparison.Ordinal));
    }

    [Fact]
    public void ByteExpectations_ComparePartlyPresentRanges()
    {
        using var before = Load((Primary("data.bin"), Bytes(10)));
        using var after = Load((Primary("data.bin"), With(Bytes(12), 9, 5)));
        var fixture = Fixture([new ExpectedChange { File = "data.bin", Offset = 8, Length = 4, Changed = false }]);

        var verification = Verify(fixture, before, after, decoder: null);

        Assert.Contains("expected unchanged", Assert.Single(verification.Expectations).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SemanticExpectation_WithoutDecoder_Fails()
    {
        using var set = Load((Primary("p.json"), Utf8("{}")));

        var verification = Verify(Fixture([new ExpectedChange { Path = "a" }]), set, set, decoder: null);

        Assert.Contains("no decoder", Assert.Single(verification.Expectations).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CheckArchive_DetectsReplacedCaptures()
    {
        var path = WriteZip(TempDir(), "a.zip", (Primary("x"), [1]));
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

        Assert.Null(FixtureVerifier.CheckArchive(new SaveRef { Archive = "a.zip", Sha256 = hash }, path));
        Assert.NotNull(FixtureVerifier.CheckArchive(new SaveRef { Archive = "a.zip", Sha256 = new string('0', 64) }, path));
    }

    private static Fixture Fixture(IReadOnlyList<ExpectedChange> expected) => new()
    {
        Id = "t",
        TitleId = Title,
        Event = "test",
        Before = new SaveRef { Archive = "b.zip" },
        After = new SaveRef { Archive = "a.zip" },
        Expected = expected,
    };

    private static FixtureVerification Verify(Fixture fixture, LoadedSaveSet before, LoadedSaveSet after, ISaveDecoder? decoder) =>
        FixtureVerifier.Verify(fixture, before.Set, after.Set, FixtureVerifier.OptionsFor(fixture, new FixtureManifest(), decoder));
}
