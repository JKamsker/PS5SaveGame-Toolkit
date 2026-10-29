using System.Security.Cryptography;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class ParserCharacterizationTests
{
    [Theory]
    [InlineData(81U, "4dcf26fa38c1ad56ba88d981de6824902589c4a6b9721431af8997545077caff")]
    [InlineData(150U, "295d4a51e39b0963081dbeff031f5effef22da9e15b0b92d085f125ad11b5a71")]
    [InlineData(151U, "7fbf94b91d5f28e5b3a1de325645db401f2fde595a524cb2c9c3bac8f8e624f1")]
    public void VersionedSlotsKeepTheirCompleteViewProtocolAndNoopBytes(uint version, string expectedDigest)
    {
        var fixture = new SyntheticSave([0, 9], version);
        using var document = fixture.Open();
        ViewNode[] views = [document.BuildView(), new SectionNode("First", document.ExpandNode("characters/0")),
            new SectionNode("Last", document.ExpandNode("characters/9")),
            new SectionNode("Inactive", document.ExpandNode("characters/4"))];
        // Updated for #40: Inferred stat/level provenance and read-only menu equipment columns.
        // Includes all protocol strings, IDs and metadata; serialization still must be identical.
        var digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(views)));
        Assert.Equal(expectedDigest, digest);
        WriteTestSupport.EqualBytes(fixture.Bytes, document);
    }

    [Theory]
    [InlineData(0, "flag", "invalid active-profile flag.")]
    [InlineData(9, "flag", "invalid active-profile flag.")]
    [InlineData(0, "version", "active profile has an empty character block.")]
    [InlineData(9, "version", "active profile has an empty character block.")]
    [InlineData(0, "name", "character name/level disagrees with its profile, or the layout is unsupported.")]
    [InlineData(9, "name", "character name/level disagrees with its profile, or the layout is unsupported.")]
    public void SlotFailuresKeepExactMessagesAndPrecedence(int index, string firstFailure, string message)
    {
        var fixture = new SyntheticSave([0, 9]);
        // Later errors must not replace the first slot/field failure.
        fixture.Write32(fixture.Players[9] + SyntheticSave.Attributes, 100);
        fixture.Write32(fixture.Players[index] + SyntheticSave.Attributes, 100);
        fixture.Bytes[fixture.Profile(index)] ^= 1;
        if (firstFailure is "flag" or "version") fixture.Write32(SyntheticSave.Slot(index), 0);
        if (firstFailure == "flag") fixture.Bytes[fixture.ActiveOffset + index] = 2;
        var error = Assert.Throws<SaveFormatException>(() => fixture.Open());
        Assert.Equal($"Slot {index}: {message}", error.Message);
    }
}
