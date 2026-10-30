using System.Buffers.Binary;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class SemanticPresetHistoryTests
{
    [Theory]
    [InlineData("PPSA01911", InventoryPlanning.ActionId)]
    [InlineData("PPSA08664", InventoryPlanning.ActionId)]
    [InlineData("PPSA18936", InventoryPlanning.ActionId)]
    [InlineData("PPSA01911", MateriaPlanning.ActionId)]
    [InlineData("PPSA08664", MateriaPlanning.ActionId)]
    [InlineData("PPSA18936", MateriaPlanning.ActionId)]
    [InlineData("PPSA01911", MateriaPlanning.StarActionId)]
    [InlineData("PPSA08664", MateriaPlanning.StarActionId)]
    [InlineData("PPSA18936", MateriaPlanning.StarActionId)]
    public void SemanticPreviewReentersF6WithExactOneUnitHistoryAndCurrentSkips(string title, string action)
    {
        using var fixture = new BulkSelectionFixture(title, action, arrange: StageOwnedMateria);
        var presets = ((FinalFantasy7Document)fixture.Document).Presets;
        var scope = action == InventoryPlanning.ActionId ? PresetScopeKind.Selected : PresetScopeKind.AllCopies;
        var preset = presets.Capture(action, fixture.Parameters, scope,
            scope == PresetScopeKind.Selected ? [fixture.Key(0)] : null, allMatches: scope == PresetScopeKind.Selected);
        var semantic = presets.Preview(preset);
        Assert.Empty(semantic.Plan.Errors);
        Assert.Contains(semantic.Plan.SkippedRows, row => row.RowKey == fixture.Key(BulkSelectionFixture.IneligibleSlot));
        var preview = fixture.Preview(semantic.Resolution.Scope);
        Assert.True(ActionPlans.ContentEquals(semantic.Plan, preview.Plan));
        fixture.AssertApplyAndHistory(preview, 0, 1, BulkSelectionFixture.UnloadedSlot);
    }

    [Theory]
    [InlineData("PPSA01911", CharacterPlanning.RecoveryActionId)]
    [InlineData("PPSA08664", CharacterPlanning.RecoveryActionId)]
    [InlineData("PPSA18936", CharacterPlanning.RecoveryActionId)]
    [InlineData("PPSA01911", CharacterPlanning.LevelActionId)]
    [InlineData("PPSA08664", CharacterPlanning.LevelActionId)]
    [InlineData("PPSA18936", CharacterPlanning.LevelActionId)]
    public void CharacterSemanticIndexesReuseRecoveryAndDerivedLevelXpAsOneHistoryUnit(string title, string action)
    {
        using var fixture = new BulkSelectionFixture(title, InventoryPlanning.ActionId, arrange: (bytes, schema) =>
        {
            for (var character = 0; character < Ff7Schema.CharacterCount; character++)
            {
                var offset = CharacterLayout.Offset(schema, character);
                bytes[offset] = character == 2 ? (byte)0 : (byte)40;
                Write(offset + CharacterLayout.ExperienceDelta, 0);
                Write(offset + CharacterLayout.HpDelta, 1);
                Write(offset + CharacterLayout.HpDelta + CharacterLayout.MaximumVitalDelta, 500);
                Write(offset + CharacterLayout.MpDelta, 0);
                Write(offset + CharacterLayout.MpDelta + CharacterLayout.MaximumVitalDelta, 50);
            }
            void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
        });
        var presets = ((FinalFantasy7Document)fixture.Document).Presets;
        Dictionary<string, FieldValue> parameters = action == CharacterPlanning.LevelActionId
            ? new() { ["level"] = FieldValue.Of(25L) } : [];
        var preset = presets.Capture(action, parameters, PresetScopeKind.Selected, ["character/0", "character/2"]);
        var semantic = presets.Preview(preset);
        Assert.Equal(PresetIdentityDomain.CharacterIndex, preset.Targets[0].Domain);
        Assert.Contains(semantic.Plan.SkippedRows, row => row.RowKey == "character/2" && row.Reason.Contains("locked", StringComparison.Ordinal));
        Assert.All(semantic.Plan.Changes, change => Assert.StartsWith("character/0/", change.ValueId));
        if (action == CharacterPlanning.LevelActionId)
            Assert.Contains(semantic.Plan.Changes, change => change.Derived && change.ValueId.EndsWith("/experience", StringComparison.Ordinal));
        var wire = parameters.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value.AsInt32()));
        var preview = fixture.Session.PlanAction(action, wire, semantic.Resolution.Scope,
            expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(ActionPlans.ContentEquals(semantic.Plan, preview.Plan));
        Assert.Equal(fixture.Original, fixture.Bytes());
        Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var edited = fixture.Bytes();
        Assert.NotEqual(fixture.Original, edited);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.False(fixture.Session.State.CanUndo);
        Assert.Equal(fixture.Original, fixture.Bytes());
        Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(edited, fixture.Bytes());
        Assert.True(fixture.Session.ApplyActionPlan(preview.PlanId!, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Stale);
    }

    [Fact]
    public void TamperedDerivedWarningsOrRescopedPlansRejectBeforeWrite()
    {
        using var fixture = new BulkSelectionFixture("PPSA08664", MateriaPlanning.StarActionId, arrange: StageOwnedMateria);
        var presets = ((FinalFantasy7Document)fixture.Document).Presets;
        var preset = presets.Capture(fixture.ActionId, fixture.Parameters, PresetScopeKind.Selected, ["materia/0"]);
        var preview = presets.Preview(preset);
        foreach (var tampered in new[] { preview.Plan with { Warnings = [] }, preview.Plan with { Changes = [] },
            preview.Plan with { Changes = preview.Plan.Changes.Select(change => change with { Derived = !change.Derived }).ToArray() } })
            Assert.False(fixture.Planner.ApplyActionPlan(fixture.ActionId, preset.Parameters, preview.Resolution.Scope, tampered).Succeeded);
        Assert.False(fixture.Planner.ApplyActionPlan(fixture.ActionId, preset.Parameters, new(["materia/1"]), preview.Plan).Succeeded);
        Assert.Equal(fixture.Original, fixture.Bytes());
    }

    private static void StageOwnedMateria(byte[] bytes, Ff7Schema schema)
    {
        if (schema.IsRemake) return;
        for (var slot = 0; slot < GameplayFields.MateriaCount; slot++)
        {
            Write(OwnedMateriaTargets.InventoryOffset + slot * GameplayLayout.MateriaStride, uint.MaxValue);
            Write(OwnedMateriaTargets.DetailOffset + slot * GameplayLayout.MateriaStride, uint.MaxValue);
        }
        var layout = GameplayLayout.For(schema);
        foreach (var slot in new[] { 0, 1, BulkSelectionFixture.UnloadedSlot, BulkSelectionFixture.IneligibleSlot })
        {
            var ownedAt = OwnedMateriaTargets.InventoryOffset + slot * GameplayLayout.MateriaStride;
            Write(ownedAt, (uint)slot + SemanticPresetFixture.Instance);
            Write(ownedAt + sizeof(uint), BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(layout.MateriaIdOffset + slot * GameplayLayout.MateriaStride)));
            Write(ownedAt + 2 * sizeof(uint), 1);
            Write(OwnedMateriaTargets.DetailOffset + slot * GameplayLayout.MateriaStride, (uint)slot + SemanticPresetFixture.Instance);
        }
        void Write(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);
    }
}
