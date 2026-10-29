using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class Sdk07MateriaScopeTests
{
    [Theory]
    [InlineData("PPSA08664", MateriaPlanning.ActionId, PresetScopeKinds.Document)]
    [InlineData("PPSA08664", MateriaPlanning.ActionId, PresetScopeKinds.AllCopies)]
    [InlineData("PPSA08664", MateriaPlanning.StarActionId, PresetScopeKinds.Document)]
    [InlineData("PPSA08664", MateriaPlanning.StarActionId, PresetScopeKinds.AllCopies)]
    [InlineData("PPSA18936", MateriaPlanning.ActionId, PresetScopeKinds.Document)]
    [InlineData("PPSA18936", MateriaPlanning.ActionId, PresetScopeKinds.AllCopies)]
    [InlineData("PPSA18936", MateriaPlanning.StarActionId, PresetScopeKinds.Document)]
    [InlineData("PPSA18936", MateriaPlanning.StarActionId, PresetScopeKinds.AllCopies)]
    public void PublicMateriaScopesWriteOnlyCurrentOwnedRows(string title, string actionId, string scope)
    {
        using var fixture = new BulkSelectionFixture(title, actionId, arrange: ArrangeOwnedAndOrphan);
        var adapter = (IPresetDocument)fixture.Document;
        var action = fixture.Session.CapturePreset(new(actionId, fixture.WireParameters, scope, null, scope == PresetScopeKinds.AllCopies));
        var resolution = adapter.ResolvePresetInstances(action.Instances);
        Assert.True(resolution.Succeeded, string.Join("; ", resolution.Instances.Select(item => item.Message)));
        var plan = adapter.PlanPreset([action], resolution);
        Assert.Empty(plan.Errors);
        Assert.Contains(plan.Changes, change => change.ValueId.StartsWith("materia/0/", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Changes, change => change.ValueId.StartsWith("materia/5/", StringComparison.Ordinal));

        var preview = fixture.Session.PlanPreset(Wrap(fixture, action, title), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(preview.Plan.Errors);
        Assert.True(fixture.Session.ApplyPresetPlan(preview.PlanId!, preview.Revision,
            cancellationToken: TestContext.Current.CancellationToken).Ok);
        var changed = fixture.Bytes();
        Assert.NotEqual(fixture.Original, changed);
        ByteChangeAssertions.OnlyOffsetsChanged(fixture.Original, changed, fixture.AllowedOffsets([0]));
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(fixture.Original, fixture.Bytes());
        Assert.False(fixture.Session.State.CanUndo);
        Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(changed, fixture.Bytes());
    }

    [Theory]
    [InlineData("PPSA08664", MateriaPlanning.ActionId, PresetScopeKinds.Document)]
    [InlineData("PPSA08664", MateriaPlanning.ActionId, PresetScopeKinds.AllCopies)]
    [InlineData("PPSA08664", MateriaPlanning.StarActionId, PresetScopeKinds.Document)]
    [InlineData("PPSA08664", MateriaPlanning.StarActionId, PresetScopeKinds.AllCopies)]
    [InlineData("PPSA18936", MateriaPlanning.ActionId, PresetScopeKinds.Document)]
    [InlineData("PPSA18936", MateriaPlanning.ActionId, PresetScopeKinds.AllCopies)]
    [InlineData("PPSA18936", MateriaPlanning.StarActionId, PresetScopeKinds.Document)]
    [InlineData("PPSA18936", MateriaPlanning.StarActionId, PresetScopeKinds.AllCopies)]
    public void OrphanOnlyMateriaScopesRemainNoOp(string title, string actionId, string scope)
    {
        using var fixture = new BulkSelectionFixture(title, actionId, arrange: ArrangeOrphanOnly);
        var adapter = (IPresetDocument)fixture.Document;
        var action = adapter.CapturePreset(new(actionId, fixture.WireParameters, scope, null, scope == PresetScopeKinds.AllCopies));
        var resolution = adapter.ResolvePresetInstances(action.Instances);
        var plan = adapter.PlanPreset([action], resolution);
        Assert.Empty(plan.Changes);
        var outcome = adapter.ApplyPreset([action], resolution, plan);
        Assert.Empty(outcome.Changes);
        Assert.Equal(fixture.Original, fixture.Bytes());
        Assert.False(fixture.Session.State.CanUndo);
    }

    [Theory]
    [InlineData("PPSA08664", MateriaPlanning.ActionId, PresetScopeKinds.Document)]
    [InlineData("PPSA08664", MateriaPlanning.ActionId, PresetScopeKinds.AllCopies)]
    [InlineData("PPSA08664", MateriaPlanning.StarActionId, PresetScopeKinds.Document)]
    [InlineData("PPSA08664", MateriaPlanning.StarActionId, PresetScopeKinds.AllCopies)]
    [InlineData("PPSA18936", MateriaPlanning.ActionId, PresetScopeKinds.Document)]
    [InlineData("PPSA18936", MateriaPlanning.ActionId, PresetScopeKinds.AllCopies)]
    [InlineData("PPSA18936", MateriaPlanning.StarActionId, PresetScopeKinds.Document)]
    [InlineData("PPSA18936", MateriaPlanning.StarActionId, PresetScopeKinds.AllCopies)]
    public void InvalidOwnedJoinCannotBecomeWritable(string title, string actionId, string scope)
    {
        using var fixture = new BulkSelectionFixture(title, actionId, arrange: ArrangeInvalidJoin);
        var adapter = (IPresetDocument)fixture.Document;
        var action = adapter.CapturePreset(new(actionId, fixture.WireParameters, scope, null, scope == PresetScopeKinds.AllCopies));
        var resolution = adapter.ResolvePresetInstances(action.Instances);
        var plan = adapter.PlanPreset([action], resolution);
        Assert.Empty(plan.Changes);
        var outcome = adapter.ApplyPreset([action], resolution, plan);
        Assert.Empty(outcome.Changes);
        Assert.Equal(fixture.Original, fixture.Bytes());
        Assert.False(fixture.Session.State.CanUndo);
    }

    [Theory]
    [InlineData("PPSA08664", MateriaPlanning.ActionId)]
    [InlineData("PPSA08664", MateriaPlanning.StarActionId)]
    [InlineData("PPSA18936", MateriaPlanning.ActionId)]
    [InlineData("PPSA18936", MateriaPlanning.StarActionId)]
    public void PublicAllCopiesReresolvesAMovedOwnedDetail(string title, string actionId)
    {
        var source = new SemanticPresetFixture(title);
        source.Materia(0, 0);
        using var captured = source.Open();
        var sourceAdapter = (IPresetDocument)captured;
        var parameters = actionId == MateriaPlanning.ActionId
            ? new Dictionary<string, JsonElement>
            {
                ["type"] = JsonSerializer.SerializeToElement("10020"),
                ["ap"] = JsonSerializer.SerializeToElement(20),
            }
            : new Dictionary<string, JsonElement>
            {
                ["type"] = JsonSerializer.SerializeToElement("10020"),
                ["stars"] = JsonSerializer.SerializeToElement(2),
            };
        var action = sourceAdapter.CapturePreset(new(actionId, parameters, PresetScopeKinds.AllCopies, null, true));

        var destination = new SemanticPresetFixture(title);
        destination.Materia(7, 7);
        using var moved = destination.Open();
        var movedAdapter = (IPresetDocument)moved;
        var resolution = movedAdapter.ResolvePresetInstances(action.Instances);
        var plan = movedAdapter.PlanPreset([action], resolution);
        Assert.Empty(plan.Errors);
        Assert.Contains(plan.Changes, change => change.ValueId.StartsWith("materia/7/", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Changes, change => change.ValueId.StartsWith("materia/0/", StringComparison.Ordinal));
    }

    private static ToolkitPreset Wrap(BulkSelectionFixture fixture, PresetAction action, string title)
    {
        var capability = ((IPresetDocument)fixture.Document).PresetCapability;
        return new("1", "materia-scope", "materia scope", new(
            "ff7r", "0.5.0", [title],
            capability.GameBuild, capability.FormatVariant, capability.ReferencePackIdentity, true, capability.PresetKind,
            capability.SchemaFingerprint, capability.FileRole, capability.SchemaVersion), [action], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
    }

    private static void ArrangeOwnedAndOrphan(byte[] bytes, Ff7Schema schema) => ArrangeMateria(bytes, schema, invalid: false, owned: true);
    private static void ArrangeOrphanOnly(byte[] bytes, Ff7Schema schema) => ArrangeMateria(bytes, schema, invalid: false, owned: false);
    private static void ArrangeInvalidJoin(byte[] bytes, Ff7Schema schema) => ArrangeMateria(bytes, schema, invalid: true, owned: true);

    private static void ArrangeMateria(byte[] bytes, Ff7Schema schema, bool invalid, bool owned)
    {
        var layout = GameplayLayout.For(schema);
        for (var slot = 0; slot < GameplayFields.MateriaCount; slot++)
        {
            Write(OwnedMateriaTargets.InventoryOffset + slot * GameplayLayout.MateriaStride, uint.MaxValue);
            Write(OwnedMateriaTargets.DetailOffset + slot * GameplayLayout.MateriaStride, uint.MaxValue);
            Write(layout.MateriaIdOffset + slot * GameplayLayout.MateriaStride, uint.MaxValue);
        }

        const uint type = SemanticPresetFixture.MateriaType;
        const uint instance = SemanticPresetFixture.Instance;
        if (owned)
        {
            Write(OwnedMateriaTargets.InventoryOffset, instance);
            Write(OwnedMateriaTargets.InventoryOffset + sizeof(uint), type);
            Write(OwnedMateriaTargets.InventoryOffset + 2 * sizeof(uint), 1);
            Write(OwnedMateriaTargets.DetailOffset, invalid ? uint.MaxValue : instance);
            Write(layout.MateriaIdOffset, type);
            Write(layout.MateriaApOffset, 0);
            bytes[layout.MateriaLevelOffset] = 1;
        }

        Write(OwnedMateriaTargets.DetailOffset + 5 * GameplayLayout.MateriaStride, instance + 1);
        Write(layout.MateriaIdOffset + 5 * GameplayLayout.MateriaStride, type);
        Write(layout.MateriaApOffset + 5 * GameplayLayout.MateriaStride, 0);
        bytes[layout.MateriaLevelOffset + 5 * GameplayLayout.MateriaStride] = 1;

        void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    }
}
