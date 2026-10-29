using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class ChildBoundaryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeclaredChildCannotBeTheFollowingSiblingOrInheritedParentBoundary(bool inherited)
    {
        var bytes = Fixture(inherited, corrupt: true);
        var container = CsavContainer.Parse(bytes);
        var error = Assert.Throws<SaveFormatException>(() => CsavTree.Parse(container));
        Assert.Equal($"CSAV node {(inherited ? 2 : 0)} child link crosses its sibling or parent boundary.", error.Message);
        Assert.Throws<SaveFormatException>(() => new Cyberpunk2077Plugin().Open(MemorySaveSet.SingleFile("sav.dat", bytes)));
        Assert.Throws<SaveFormatException>(() => container.Rebuild(container.Payload));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidChildAndSiblingLinksKeepExactTreeAndNoOpBytes(bool inherited)
    {
        var bytes = Fixture(inherited, corrupt: false);
        var container = CsavContainer.Parse(bytes);
        var tree = CsavTree.Parse(container);
        foreach (var node in tree.Nodes)
            Assert.Equal(node.ChildId, node.Children.Count == 0 ? -1 : node.Children[0]);
        int[] roots = inherited ? [0, 3] : [0];
        int[] children = inherited ? [1, 2] : [1];
        Assert.Equal(roots, tree.Roots);
        Assert.Equal(children, tree.Nodes[0].Children);
        Assert.Equal(bytes, container.Rebuild(container.Payload));
        using var document = new Cyberpunk2077Plugin().Open(MemorySaveSet.SingleFile("sav.dat", bytes));
        _ = document.BuildView();
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
    }

    private static byte[] Fixture(bool inherited, bool corrupt)
    {
        var count = inherited ? 4 : 2;
        var payload = new byte[count * sizeof(int)];
        for (var i = 0; i < count; i++) CsavCorruptionFixture.SetWord(payload, i * sizeof(int), i);
        FixtureNode[] nodes = inherited
            ? [new("parent", 3, 1, 0, 12), new("first", 2, -1, 4, 4),
                new("last", -1, corrupt ? 3 : -1, 8, 4), new("root", -1, -1, 12, 4)]
            : [new("parent", corrupt ? 1 : -1, 1, 0, corrupt ? 4 : 8), new("child", -1, -1, 4, 4)];
        return CsavCorruptionFixture.Create(payload, nodes);
    }
}
