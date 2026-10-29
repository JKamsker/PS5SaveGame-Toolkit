using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AtomicHeart.Tests;

public sealed class AtomicHeartInspectorPerformanceTests
{
    [Fact]
    public void UnopenedLeafIdentitiesHaveBoundedAllocations()
    {
        var index = Index(1000, new string('x', 512), 3);
        _ = Views(index);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var views = Views(index);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(views);
        Assert.True(allocated < 1_500_000, $"Inspector construction allocated {allocated:N0} bytes.");
    }

    [Fact]
    public void DuplicateNestedAndPagedIdentitiesRetainExactPaths()
    {
        var children = Enumerable.Range(0, 101).Select(i => Scalar("A/B", i == 100 ? 1 : 0)).ToArray();
        var parent = Scalar("Parent space", 3) with { Children = children };
        var record = Record("owner/record", [parent]);
        var views = Views(new([], [], [record], []));
        var objectGroup = Assert.Single(views.Expand("inspect/object/0").OfType<SectionNode>());
        var childGroup = Assert.IsType<SectionNode>(Assert.Single(views.Expand(objectGroup.LazyId!)));
        var first = views.Expand(childGroup.LazyId!);
        var fields = first.OfType<FieldNode>().ToArray();
        const string prefix = "owner/record/Parent%20space[3]/A%2FB[0]";
        Assert.Equal(99, fields.Length);
        Assert.Equal(prefix, fields[0].ValueId);
        Assert.Equal(prefix + "#2", fields[1].ValueId);
        Assert.Equal(prefix + "#99", fields[^1].ValueId);
        var next = Assert.Single(first.OfType<SectionNode>());
        var tail = views.Expand(next.LazyId!).Cast<FieldNode>().ToArray();
        Assert.Equal([prefix + "#100", "owner/record/Parent%20space[3]/A%2FB[1]"], tail.Select(f => f.ValueId));
        Assert.Equal(fields.Select(f => f.ValueId), views.Expand(childGroup.LazyId!).OfType<FieldNode>().Select(f => f.ValueId));
    }

    [Theory]
    [InlineData(4000, false)]
    [InlineData(4096, true)]
    public void AggregatePathBudgetIsStillCheckedBeforeAnyExpansion(int parentLength, bool rejects)
    {
        var index = Index(1, new string('x', parentLength), 4096);
        if (rejects)
            Assert.Equal("Property identity text exceeds the index budget.", Assert.Throws<SaveFormatException>(() => Views(index)).Message);
        else Assert.NotNull(Views(index));
    }

    private static RecordViews Views(RecordIndex index) => new(index,
        (id, label, spec, get) => new(id, label, spec, get()), property => property.Value!.Value);

    private static RecordIndex Index(int count, string prefix, int members) => new([], [],
        Enumerable.Range(0, count).Select(i => Record(prefix + i,
            Enumerable.Range(0, members).Select(n => Scalar("Value", n)).ToArray())).ToArray(), []);

    private static ObjectRecord Record(string id, IReadOnlyList<TaggedProperty> members) =>
        new(id, RecordKinds.Subsystem, "owner", "Name", "Class", "", default, default, [], members, default, []);

    private static TaggedProperty Scalar(string name, int arrayIndex) =>
        new(name, "IntProperty", "", arrayIndex, default, default, FieldValue.Of(1));
}
