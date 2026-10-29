using System.Security.Cryptography;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.EldenRing.Tests;

public sealed class ParserCharacterizationTests
{
    [Theory]
    [InlineData(81U, "a2ce165673b9668f131a11d8eb32bbea8b77746d4d392e209d35bee8f59b73b8")]
    [InlineData(150U, "fc2d4c5852283f12876c807faf2bf951d29874e20a103cbd63d129464c4a31f7")]
    [InlineData(151U, "c0c0e26c8e47445cabab51b3ec9fe8289102b3189e84c7b86c359a9a16ba54a5")]
    public void VersionedSlotsKeepTheirCompleteViewProtocolAndNoopBytes(uint version, string expectedDigest)
    {
        var fixture = new SyntheticSave([0, 9], version);
        using var document = fixture.Open();
        ViewNode[] views = [document.BuildView(), new SectionNode("First", document.ExpandNode("characters/0")),
            new SectionNode("Last", document.ExpandNode("characters/9")),
            new SectionNode("Inactive", document.ExpandNode("characters/4"))];
        // Captured on 207f6e9 before refactoring; includes all protocol strings, IDs and metadata.
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
