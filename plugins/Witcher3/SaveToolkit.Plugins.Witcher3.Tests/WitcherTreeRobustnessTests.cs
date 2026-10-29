using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherTreeRobustnessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndexedChildrenCannotHideMalformedArchive(bool corruptLength)
    {
        var archive = RobustnessSave.Archive(1);
        if (corruptLength) { RobustnessSave.WriteInt(archive, 2, int.MaxValue); }
        else { RobustnessSave.WriteInt(archive, 12, int.MaxValue); }
        var payload = RobustnessSave.Payload(archive, new(16, archive.Length), new(22, archive.Length - 6));
        Assert.Throws<SaveFormatException>(() => RobustnessSave.Open(payload));
    }

    [Theory]
    [InlineData(128, 0)]
    [InlineData(1, 127)]
    [InlineData(64, 64)]
    public void CombinedIndexedAndEmbeddedDepthCannotExceed128(int indexed, int blocks)
    {
        var archive = RobustnessSave.Archive(blocks);
        var variables = new byte[(indexed - 1) * 4 + archive.Length];
        var entries = new List<Sav3Entry>();
        for (var i = 0; i < indexed - 1; i++)
        {
            "BS\x01\0"u8.CopyTo(variables.AsSpan(i * 4));
            entries.Add(new(16 + i * 4, variables.Length - i * 4));
        }
        archive.CopyTo(variables, (indexed - 1) * 4);
        entries.Add(new(16 + (indexed - 1) * 4, archive.Length));
        var payload = RobustnessSave.Payload(variables, entries.ToArray());
        using var valid = RobustnessSave.Open(payload);

        // Adding one embedded level crosses the same total bound, regardless of the indexed split.
        var deeper = RobustnessSave.Archive(blocks + 1);
        variables = [.. variables.AsSpan(0, (indexed - 1) * 4), .. deeper];
        var deeperEntries = entries.Select(e => e with { Size = e.Size + 10 }).ToArray();
        Assert.Throws<SaveFormatException>(() => RobustnessSave.Open(RobustnessSave.Payload(variables, deeperEntries)));
    }
}
