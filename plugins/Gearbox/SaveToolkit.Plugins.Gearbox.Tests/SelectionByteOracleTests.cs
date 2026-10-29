using System.Text.Json;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class SelectionByteOracleTests
{
    private const int UnknownFieldNumber = 20000;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteLiveOracle_RejectsMissingMembersAndCorruptionAtCleanBaselineAndAfterUndo(bool wonderlands)
    {
        var observer = new SelectionGroupObserver();
        using var workspace = SelectionTestData.Workspace(wonderlands, observer);
        var anchor = workspace.Set.AllDiskFiles.Single(file => file.FileName == "1");
        var session = workspace.OpenGroup(Assert.Single(workspace.ProposeGroups(anchor.Id, "gearbox")).Token).Session;
        _ = session.BuildView();
        var expected = workspace.Set.AllDiskFiles.ToDictionary(file => file.RelativePath, file => file.ReadAllBytes());
        Assert.Equal(2, expected.Count);
        Assert.Empty(session.Serialize(cancellationToken: TestContext.Current.CancellationToken));
        SelectionByteAssertions.Equal(observer.Current, expected);
        // The old empty-delta oracle passed both of these mistakes without comparing a byte.
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => SelectionByteAssertions.Equal(expected, new Dictionary<string, byte[]>()));
        var missing = expected.Take(1).ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => SelectionByteAssertions.Equal(observer.Current, missing));
        var profilePath = workspace.Set.AllDiskFiles.Single(file => file.FileName == "profile").RelativePath;
        var corruptExpected = expected.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        corruptExpected[profilePath][^1] ^= 1; // Deliberately wrong unknown trailer byte, not a writer expectation.
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => SelectionByteAssertions.Equal(observer.Current, corruptExpected));

        var source = session.QueryCollection("character/inventory_items", new() { Limit = 1 },
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        var preview = session.PlanAction("transfer/deposit", new Dictionary<string, JsonElement>(), new ActionScope([source.Rows[0].Key]),
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(preview.Plan.Errors);
        Assert.True(session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        Assert.True(session.Undo().Ok);
        SelectionByteAssertions.Equal(observer.Current, expected);
        Assert.Empty(session.Serialize(cancellationToken: TestContext.Current.CancellationToken));

        // Inject an unrelated unknown-field change after undo. Clean export remains empty, but the
        // complete live oracle must reject the corrupted member rather than report successful undo.
        observer.Current.Profile.Root.Add(new ProtoField(UnknownFieldNumber, 0, null, [], 1UL, null));
        Assert.Empty(session.Serialize(cancellationToken: TestContext.Current.CancellationToken));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => SelectionByteAssertions.Equal(observer.Current, expected));
    }
}
