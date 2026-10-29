using System.Globalization;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests.References;

public sealed class PackSnapshotBaselineTests
{
    private static readonly string OriginalHash = new('a', 64);
    private static readonly string ChangedHash = new('b', 64);

    [Theory]
    [InlineData("en-US")]
    [InlineData("sv-SE")]
    [InlineData("th-TH")]
    public void RecordFingerprintUsesHistoricalCultureAndRestoresAmbientCulture(string cultureName)
    {
        var store = new ReferencePackStore([new ItemsPlugin()]);
        var pack = Assert.Single(store.Packs, pack => pack.Id == "test.items.en");
        var manifest = Assert.Single(EmbeddedPackSnapshotTests.Manifests(typeof(ItemsPlugin).Assembly),
            entry => entry.Manifest.GetProperty("id").GetString() == pack.Id).Manifest;
        string expected;
        using (new TestCulture(CultureInfo.InvariantCulture)) expected = PackSnapshotBaseline.Hash(pack, manifest);

        using var ambient = new TestCulture(CultureInfo.GetCultureInfo(cultureName));
        Assert.Equal(expected, PackSnapshotBaseline.Hash(pack, manifest));
        Assert.Equal(cultureName, CultureInfo.CurrentCulture.Name);
        Assert.Equal(cultureName, CultureInfo.CurrentUICulture.Name);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void BaselineReadsBothLineEndings(string newline)
    {
        using var reader = new StringReader($"first {OriginalHash}{newline}second {ChangedHash}{newline}");
        var baseline = PackSnapshotBaseline.Read(reader);
        Assert.Equal(OriginalHash, baseline["first"]);
        Assert.Equal(ChangedHash, baseline["second"]);
    }

    [Fact]
    public void UnchangedPacksHaveNoDifferences()
    {
        var pins = new Dictionary<string, string> { ["first"] = OriginalHash };
        Assert.Empty(PackSnapshotBaseline.Differences(pins, pins));
    }

    [Fact]
    public void ChangedPackReportsOnlyItsLineAndThePrintCommand()
    {
        var pins = new Dictionary<string, string> { ["first"] = OriginalHash, ["second"] = OriginalHash };
        var current = new Dictionary<string, string>(pins) { ["second"] = ChangedHash };
        var message = Assert.Single(PackSnapshotBaseline.Differences(pins, current));
        Assert.Contains("edit the line beginning 'second '", message, StringComparison.Ordinal);
        Assert.Contains($"second {ChangedHash}", message, StringComparison.Ordinal);
        Assert.DoesNotContain("first", message, StringComparison.Ordinal);
        AssertRepinInstructions(message);
    }

    [Fact]
    public void NewPackMustBeExplicitlyAdded()
    {
        using var reader = new StringReader(string.Empty);
        var pins = PackSnapshotBaseline.Read(reader);
        var current = new Dictionary<string, string> { ["new.pack"] = ChangedHash };
        var message = Assert.Single(PackSnapshotBaseline.Differences(pins, current));
        Assert.Contains("add this line", message, StringComparison.Ordinal);
        Assert.Contains($"new.pack {ChangedHash}", message, StringComparison.Ordinal);
        AssertRepinInstructions(message);
    }

    [Fact]
    public void RemovedPackCannotLeaveAStalePin()
    {
        var pins = new Dictionary<string, string> { ["removed.pack"] = OriginalHash };
        var message = Assert.Single(PackSnapshotBaseline.Differences(pins, new Dictionary<string, string>()));
        Assert.Contains("delete the line beginning 'removed.pack '", message, StringComparison.Ordinal);
        AssertRepinInstructions(message);
    }

    [Fact]
    public void DuplicatePinsReportTheirFileAndLine()
    {
        using var reader = new StringReader($"same {OriginalHash}\nsame {ChangedHash}\n");
        var error = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => PackSnapshotBaseline.Read(reader));
        Assert.Contains($"{PackSnapshotBaseline.FilePath}:2", error.Message, StringComparison.Ordinal);
        Assert.Contains("duplicate pack id 'same'", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pack")]
    [InlineData("pack not-a-hash")]
    public void MalformedPinsAreRejected(string line)
    {
        using var reader = new StringReader(line + "\n");
        var error = Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => PackSnapshotBaseline.Read(reader));
        Assert.Contains($"{PackSnapshotBaseline.FilePath}:1", error.Message, StringComparison.Ordinal);
    }

    private static void AssertRepinInstructions(string message)
    {
        Assert.Contains(PackSnapshotBaseline.FilePath, message, StringComparison.Ordinal);
        Assert.Contains(PackSnapshotBaseline.PrintCommand, message, StringComparison.Ordinal);
    }
}
