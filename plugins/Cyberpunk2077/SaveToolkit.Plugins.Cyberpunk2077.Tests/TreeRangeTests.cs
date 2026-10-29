using System.Security.Cryptography;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class TreeRangeTests
{
    [Theory]
    // Captured before consolidating this fixture with the shared independent CSAV encoder.
    [InlineData(false, false, "741A2930BD16E72EBFF6AA77980CE06148471E06A40F1CC35D547D67264102D1")]
    [InlineData(false, true, "7733A8C2525A9FBBA8D16C1C2FAD516FA40C4A25CCEBA887B498CBB72E2AE412")]
    [InlineData(true, false, "59A13AD7FAD2B3057543E759762C249BB0C95660B5690FF6C56C7DFCEC456549")]
    [InlineData(true, true, "2DD797D188F9E2B27835FDD7C52A0158AFD9C618DD857A791D9F13D5840CFAA4")]
    public void RangeFixturesKeepTheirExactWireBytes(bool nested, bool overlap, string expected) =>
        Assert.Equal(expected, Convert.ToHexString(SHA256.HashData(Fixture(nested, overlap))));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SiblingOverlapIsRejectedWhileAdjacentRangesAndParentContainmentAreAllowed(bool nested)
    {
        var valid = CsavContainer.Parse(Fixture(nested, overlap: false));
        Assert.Equal(nested ? 3 : 2, CsavTree.Parse(valid).Nodes.Count);
        Assert.Equal(valid.Original, valid.Rebuild(valid.Payload));
        var invalid = CsavContainer.Parse(Fixture(nested, overlap: true));
        var error = Assert.Throws<SaveFormatException>(() => CsavTree.Parse(invalid));
        Assert.Contains("sibling node ranges overlap", error.Message);
        Assert.Throws<SaveFormatException>(() => invalid.Rebuild(invalid.Payload));
    }

    private static byte[] Fixture(bool nested, bool overlap)
    {
        const int payloadSize = 24;
        var count = nested ? 3 : 2;
        var payload = new byte[payloadSize];
        var nodes = new FixtureNode[count];
        for (var i = 0; i < count; i++)
        {
            CsavCorruptionFixture.SetWord(payload, i * sizeof(int), i);
            var parent = nested && i == 0;
            nodes[i] = new FixtureNode("x", parent || i == count - 1 ? -1 : i + 1, parent ? 1 : -1,
                i * sizeof(int), parent ? payloadSize : overlap && i == count - 2 ? 8 : 4);
        }
        return CsavCorruptionFixture.Create(payload, nodes);
    }
}
