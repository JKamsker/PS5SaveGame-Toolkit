namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class SemanticMateriaPresetTests
{
    [Fact]
    public void RepeatedMalformedRelationshipsStayBoundedAndDoNotPickAFirstDetail()
    {
        var fixture = new SemanticPresetFixture();
        for (var slot = 0; slot < GameplayFields.MateriaCount; slot++) fixture.Materia(slot, slot);
        using var document = fixture.Open();
        var preset = document.Presets.Capture(MateriaPlanning.StarActionId, SemanticPresetFixture.StarParameters,
            PresetScopeKind.AllCopies);
        var preview = document.Presets.Preview(preset);
        var target = Assert.Single(preview.Resolution.Targets);
        Assert.Equal("duplicate-detail", target.Reason);
        Assert.Equal(GameplayFields.MateriaCount, target.Candidates.Count);
        Assert.All(target.Candidates, candidate => Assert.Equal(GameplayFields.MateriaCount, candidate.OwnedInventorySlots.Count));
        Assert.Empty(preview.Plan.Changes);
        SemanticPresetFixture.Unchanged(document, fixture.Bytes);
    }

    [Fact]
    public void MissingAllCopiesIsExplicitAndDoesNotUseUnownedDetailRows()
    {
        var fixture = new SemanticPresetFixture();
        fixture.Detail(5);
        using var document = fixture.Open();
        var preset = document.Presets.Capture(MateriaPlanning.StarActionId, SemanticPresetFixture.StarParameters,
            PresetScopeKind.AllCopies);
        var preview = document.Presets.Preview(preset);
        Assert.Equal("missing-owned-copies", Assert.Single(preview.Resolution.Targets).Reason);
        Assert.Equal(PresetIdentityDomain.MateriaType, preview.Resolution.Targets[0].Target.Domain);
        Assert.Empty(preview.Plan.Changes);
        SemanticPresetFixture.Unchanged(document, fixture.Bytes);
    }

    [Fact]
    public void InstanceCaptureMapsToMovedDetailAndNotOwnedInventoryOrOldPhysicalSlot()
    {
        var source = new SemanticPresetFixture();
        source.Materia(3, 7);
        using var captured = source.Open();
        var preset = captured.Presets.Capture(MateriaPlanning.StarActionId, SemanticPresetFixture.StarParameters,
            PresetScopeKind.Selected, ["materia/7"]);
        var capturedTarget = Assert.Single(preset.Targets);
        Assert.Equal(PresetIdentityDomain.MateriaInstance, capturedTarget.Domain);
        Assert.Equal(SemanticPresetFixture.Instance, capturedTarget.Id);
        Assert.Equal(SemanticPresetFixture.MateriaType, capturedTarget.TypeId);
        Assert.NotNull(capturedTarget.Source);
        var destination = new SemanticPresetFixture();
        destination.Materia(91, 203);
        using var moved = destination.Open();
        var preview = moved.Presets.Preview(preset);
        Assert.Empty(preview.Plan.Errors);
        Assert.Equal("materia/203", Assert.Single(preview.Resolution.Scope.RowKeys!));
        Assert.All(preview.Plan.Changes, change => Assert.StartsWith("materia/203/", change.ValueId));
        Assert.Contains(preview.Plan.Changes, change => change.Derived && change.ValueId.EndsWith("/ap", StringComparison.Ordinal));
        Assert.Contains(preview.Plan.Changes, change => !change.Derived && change.ValueId.EndsWith("/level", StringComparison.Ordinal));
        var candidate = Assert.Single(Assert.Single(preview.Resolution.Targets).Candidates);
        Assert.Equal(0, candidate.OwnerIndex);
        Assert.Contains("RyudoSynbios", candidate.Source, StringComparison.Ordinal);
        SemanticPresetFixture.Unchanged(moved, destination.Bytes);
    }

    [Theory]
    [InlineData("missing-detail")]
    [InlineData("duplicate-detail")]
    [InlineData("duplicate-inventory")]
    [InlineData("invalid-owned-count")]
    [InlineData("detail-type-mismatch")]
    public void UnresolvedJoinsPreserveRawIdentityAndEveryByte(string reason)
    {
        var source = new SemanticPresetFixture();
        source.Materia(2, 4);
        using var captured = source.Open();
        var preset = captured.Presets.Capture(MateriaPlanning.StarActionId, SemanticPresetFixture.StarParameters,
            PresetScopeKind.Selected, ["materia/4"]);
        var destination = new SemanticPresetFixture();
        destination.Materia(10, 20);
        switch (reason)
        {
            case "missing-detail": destination.Write(OwnedMateriaTargets.DetailOffset + 20 * GameplayLayout.MateriaStride, uint.MaxValue); break;
            case "duplicate-detail": destination.Detail(21); break;
            case "duplicate-inventory": destination.Materia(11, 20); break;
            case "invalid-owned-count": destination.Write(OwnedMateriaTargets.InventoryOffset + 10 * GameplayLayout.MateriaStride + 8, 2); break;
            case "detail-type-mismatch": destination.Write(destination.Layout.MateriaIdOffset + 20 * GameplayLayout.MateriaStride, 10001); break;
        }
        using var unresolved = destination.Open();
        var preview = unresolved.Presets.Preview(preset);
        var target = Assert.Single(preview.Resolution.Targets);
        Assert.Equal(PresetTargetStatus.Unresolved, target.Status);
        Assert.Equal(reason, target.Reason);
        Assert.All(target.Candidates, candidate => Assert.Equal(SemanticPresetFixture.Instance, candidate.InstanceId));
        Assert.Equal(reason == "duplicate-detail" ? 2 : 1, target.Candidates.Count);
        Assert.All(target.Candidates, candidate => Assert.Equal(reason == "duplicate-inventory" ? 2 : 1, candidate.OwnedInventorySlots.Count));
        if (reason == "missing-detail") Assert.Null(Assert.Single(target.Candidates).Slot);
        if (reason == "detail-type-mismatch") Assert.Equal(10001U, Assert.Single(target.Candidates).DetailTypeId);
        Assert.Empty(preview.Resolution.Scope.RowKeys!);
        Assert.Empty(preview.Plan.Changes);
        Assert.Throws<ArgumentException>(() => unresolved.Presets.Resolve(preset,
            new Dictionary<int, string> { [0] = "materia/20" }));
        SemanticPresetFixture.Unchanged(unresolved, destination.Bytes);
    }

    [Fact]
    public void MissingInstanceDoesNotFallbackToSameTypeAndAllCopiesReportsUnresolvedCopies()
    {
        var fixture = new SemanticPresetFixture();
        fixture.Materia(1, 5);
        fixture.Materia(2, 8, SemanticPresetFixture.Instance + 1);
        fixture.Detail(9, SemanticPresetFixture.Instance + 1);
        using var document = fixture.Open();
        var all = document.Presets.Capture(MateriaPlanning.StarActionId, SemanticPresetFixture.StarParameters, PresetScopeKind.AllCopies);
        var preview = document.Presets.Preview(all);
        Assert.Equal(2, preview.Resolution.Targets.Count);
        Assert.Contains(preview.Resolution.Targets, target => target.Reason == "duplicate-detail");
        Assert.Equal("materia/5", Assert.Single(preview.Resolution.Scope.RowKeys!));
        var missing = all with { ScopeKind = PresetScopeKind.Selected,
            Targets = [new(PresetIdentityDomain.MateriaInstance, SemanticPresetFixture.Instance + 2, SemanticPresetFixture.MateriaType)] };
        preview = document.Presets.Preview(missing);
        Assert.Equal(PresetTargetStatus.Missing, Assert.Single(preview.Resolution.Targets).Status);
        Assert.Empty(preview.Plan.Changes);
        SemanticPresetFixture.Unchanged(document, fixture.Bytes);
    }

    [Theory]
    [InlineData(10020U, 0)] // AP/star incoherence.
    [InlineData(14011U, 1)] // Titan: summon zero-threshold rules are not ordinary materia rules.
    public void CurrentF6EligibilityRemainsAuthoritativeForSemanticTargets(uint type, byte stars)
    {
        var fixture = new SemanticPresetFixture();
        fixture.Materia(1, 5, type: type, stars: stars);
        using var document = fixture.Open();
        var parameters = SemanticPresetFixture.StarParameters;
        parameters["type"] = SaveToolkit.Sdk.Values.FieldValue.Of(type.ToString(System.Globalization.CultureInfo.InvariantCulture));
        parameters["stars"] = SaveToolkit.Sdk.Values.FieldValue.Of(1L);
        var preset = document.Presets.Capture(MateriaPlanning.StarActionId, parameters, PresetScopeKind.Selected, ["materia/5"]);
        var preview = document.Presets.Preview(preset);
        Assert.Equal(PresetTargetStatus.Matched, Assert.Single(preview.Resolution.Targets).Status);
        Assert.Empty(preview.Plan.Changes);
        Assert.Equal("Empty, unknown, summon or AP/level mismatch.", Assert.Single(preview.Plan.SkippedRows).Reason);
        SemanticPresetFixture.Unchanged(document, fixture.Bytes);
    }

    [Fact]
    public void RemakeSelectedMateriaDoesNotInventAnInstanceAndAllCopiesStillUsesExistingPlanner()
    {
        var fixture = new SemanticPresetFixture("PPSA01911");
        fixture.Write(fixture.Layout.MateriaIdOffset, 10004);
        fixture.Bytes[fixture.Layout.MateriaLevelOffset] = 1;
        using var document = fixture.Open();
        var parameters = SemanticPresetFixture.StarParameters;
        parameters["type"] = SaveToolkit.Sdk.Values.FieldValue.Of("10004");
        Assert.Throws<ArgumentException>(() => document.Presets.Capture(MateriaPlanning.StarActionId, parameters,
            PresetScopeKind.Selected, ["materia/0"]));
        var preset = document.Presets.Capture(MateriaPlanning.StarActionId, parameters, PresetScopeKind.AllCopies);
        Assert.Equal(2, document.Presets.Preview(preset).Plan.Changes.Count);
        SemanticPresetFixture.Unchanged(document, fixture.Bytes);
    }
}
