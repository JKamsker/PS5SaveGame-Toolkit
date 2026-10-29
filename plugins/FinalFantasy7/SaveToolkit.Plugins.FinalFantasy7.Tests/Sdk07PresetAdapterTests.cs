using System.Text.Json;
using System.Buffers.Binary;
using SaveToolkit.Core.Presets;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class Sdk07PresetAdapterTests
{
    [Fact]
    public void CapabilityPublishesOnlyTheFiveBoundedF6ActionsAndExactVariantRole()
    {
        using var fixture = new BulkSelectionFixture("PPSA08664", MateriaPlanning.ActionId);
        var adapter = Assert.IsAssignableFrom<IPresetDocument>(fixture.Document);
        Assert.Equal("rebirth", adapter.PresetCapability.FormatVariant);
        Assert.Equal("gameplay", adapter.PresetCapability.FileRole);
        Assert.Null(adapter.PresetCapability.GameBuild);
        Assert.Equal(1, adapter.PresetCapability.MaxActions);
        Assert.Equal(5, adapter.PresetCapability.Actions.Count);
        Assert.Contains(PresetScopeKinds.Selected, adapter.PresetCapability.Actions.Single(a => a.ActionId == InventoryPlanning.ActionId).SupportedScopes);
        Assert.DoesNotContain("inventoryTable", adapter.PresetCapability.Actions.SelectMany(a => a.ParameterIds));
    }

    [Fact]
    public void EveryPublishedActionCapturesTypedParametersWithoutAliases()
    {
        using var fixture = new BulkSelectionFixture("PPSA08664", MateriaPlanning.ActionId);
        var adapter = (IPresetDocument)fixture.Document;
        var requests = new[]
        {
            new PresetCaptureRequest(InventoryPlanning.ActionId,
                new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement("20") }),
            new PresetCaptureRequest(MateriaPlanning.ActionId,
                new Dictionary<string, JsonElement> { ["type"] = JsonSerializer.SerializeToElement(fixture.TypeText), ["ap"] = JsonSerializer.SerializeToElement(20) }),
            new PresetCaptureRequest(MateriaPlanning.StarActionId,
                new Dictionary<string, JsonElement> { ["type"] = JsonSerializer.SerializeToElement(fixture.TypeText), ["stars"] = JsonSerializer.SerializeToElement(1) }),
            new PresetCaptureRequest(CharacterPlanning.RecoveryActionId, new Dictionary<string, JsonElement>()),
            new PresetCaptureRequest(CharacterPlanning.LevelActionId,
                new Dictionary<string, JsonElement> { ["level"] = JsonSerializer.SerializeToElement(25) }),
        };
        foreach (var request in requests)
        {
            Assert.Null(PresetContractValidation.ValidateCapture(adapter.PresetCapability, request));
            var action = adapter.CapturePreset(request);
            Assert.Null(PresetContractValidation.ValidateCapturedAction(adapter.PresetCapability, request, action));
        }
    }

    [Fact]
    public void UnknownMateriaIdPassesHostShapeButPluginRejectsBeforeAnyPlanOrWrite()
    {
        using var fixture = new BulkSelectionFixture("PPSA08664", MateriaPlanning.ActionId);
        var adapter = (IPresetDocument)fixture.Document;
        var request = new PresetCaptureRequest(MateriaPlanning.ActionId,
            new Dictionary<string, JsonElement>
            {
                ["type"] = JsonSerializer.SerializeToElement("999999"),
                ["ap"] = JsonSerializer.SerializeToElement("20"),
            });
        Assert.Null(PresetContractValidation.ValidateCapture(adapter.PresetCapability, request));
        Assert.Throws<ArgumentException>(() => adapter.CapturePreset(request));
        Assert.Equal(fixture.Original, fixture.Bytes());
    }

    [Fact]
    public void TamperedIdentityMetadataIsUnsupportedBeforePlanning()
    {
        using var fixture = new BulkSelectionFixture("PPSA08664", InventoryPlanning.ActionId);
        var adapter = (IPresetDocument)fixture.Document;
        var action = adapter.CapturePreset(new(InventoryPlanning.ActionId,
            new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement(99) },
            PresetScopeKinds.Selected, [fixture.Key(0)]));
        var tampered = action with { Instances = [action.Instances.Single() with { Role = "character-index" }] };
        var resolution = adapter.ResolvePresetInstances(tampered.Instances);
        Assert.Equal(PresetInstanceResolutionStatus.Unsupported, Assert.Single(resolution.Instances).Status);
        var plan = adapter.PlanPreset([tampered], resolution);
        Assert.NotEmpty(plan.Errors);
        Assert.Equal(fixture.Original, fixture.Bytes());
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("4294967296")]
    [InlineData("0x10020")]
    [InlineData("4294967295")]
    [InlineData("999999")]
    public void InvalidMateriaTypesRejectPlanAndApplyWithoutPlannerOrHistory(string type)
    {
        using var fixture = new BulkSelectionFixture("PPSA08664", MateriaPlanning.ActionId);
        var adapter = (IPresetDocument)fixture.Document;
        var action = new PresetAction(MateriaPlanning.ActionId, new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement(type),
            ["ap"] = JsonSerializer.SerializeToElement("20"),
        }, [], PresetScopeKinds.Document);
        var resolution = new SaveToolkit.Sdk.PresetResolution([]);
        var plan = adapter.PlanPreset([action], resolution);
        Assert.NotEmpty(plan.Errors);
        var outcome = adapter.ApplyPreset([action], resolution, plan);
        Assert.False(outcome.Succeeded);
        Assert.Equal(fixture.Original, fixture.Bytes());
        Assert.False(fixture.Session.State.CanUndo);
    }

    [Theory]
    [InlineData(CharacterPlanning.RecoveryActionId)]
    [InlineData(CharacterPlanning.LevelActionId)]
    public void SelectedCharacterActionUsesOnlySelectedCharacterAndOneHistoryUnit(string actionId)
    {
        using var fixture = new BulkSelectionFixture("PPSA08664", InventoryPlanning.ActionId, arrange: ArrangeCharacters);
        var adapter = (IPresetDocument)fixture.Document;
        var parameters = actionId == CharacterPlanning.LevelActionId
            ? new Dictionary<string, JsonElement> { ["level"] = JsonSerializer.SerializeToElement(25) }
            : new Dictionary<string, JsonElement>();
        var action = fixture.Session.CapturePreset(new(actionId, parameters, PresetScopeKinds.Selected, ["character/0"]));
        var resolution = adapter.ResolvePresetInstances(action.Instances);
        var plan = adapter.PlanPreset([action], resolution);
        Assert.NotEmpty(plan.Changes);
        Assert.All(plan.Changes, change => Assert.StartsWith("character/0/", change.ValueId));
        Assert.DoesNotContain(plan.Changes, change => change.ValueId.StartsWith("character/1/", StringComparison.Ordinal));
        if (actionId == CharacterPlanning.RecoveryActionId)
            Assert.Equal(["character/0/hp", "character/0/mp"], plan.Changes.Select(change => change.ValueId).ToArray());
        else
            Assert.Equal(["character/0/level", "character/0/experience"], plan.Changes.Select(change => change.ValueId).ToArray());

        var preset = new ToolkitPreset("1", "selected-character", "selected character", new(
            "ff7r", "0.5.0", ["PPSA08664"], adapter.PresetCapability.GameBuild, adapter.PresetCapability.FormatVariant,
            adapter.PresetCapability.ReferencePackIdentity, true, adapter.PresetCapability.PresetKind,
            adapter.PresetCapability.SchemaFingerprint, adapter.PresetCapability.FileRole, adapter.PresetCapability.SchemaVersion),
            [action], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var preview = fixture.Session.PlanPreset(preset, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(preview.Plan.Errors);
        Assert.True(fixture.Session.ApplyPresetPlan(preview.PlanId!, preview.Revision,
            cancellationToken: TestContext.Current.CancellationToken).Ok);
        var changed = fixture.Bytes();
        Assert.NotEqual(fixture.Original, changed);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(fixture.Original, fixture.Bytes());
        Assert.False(fixture.Session.State.CanUndo);
        Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(changed, fixture.Bytes());
    }

    [Fact]
    public void HostCaptureResolvePlanApplyUsesCurrentCandidateAndOneUndoUnit()
    {
        using var fixture = new BulkSelectionFixture("PPSA08664", InventoryPlanning.ActionId);
        var adapter = (IPresetDocument)fixture.Document;
        var capture = fixture.Session.CapturePreset(new(
            InventoryPlanning.ActionId,
            new Dictionary<string, JsonElement> { ["quantity"] = JsonSerializer.SerializeToElement(99) },
            PresetScopeKinds.Selected, [fixture.Key(0)]));
        var compatibility = adapter.PresetCapability;
        var preset = new ToolkitPreset("1", "preset-id", "restock", new(
            "ff7r", "0.5.0", ["PPSA08664"], compatibility.GameBuild, compatibility.FormatVariant,
            compatibility.ReferencePackIdentity, true, compatibility.PresetKind, compatibility.SchemaFingerprint,
            compatibility.FileRole, compatibility.SchemaVersion), [capture], DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

        var unresolved = fixture.Session.PlanPreset(preset, cancellationToken: TestContext.Current.CancellationToken);
        var instance = Assert.Single(unresolved.Resolution.Instances);
        Assert.Equal(PresetInstanceResolutionStatus.Ambiguous, instance.Status);
        var selected = instance.Candidates.Single(candidate => candidate.StableId.EndsWith("/1", StringComparison.Ordinal));
        var preview = fixture.Session.PlanPreset(preset, choices:
            new Dictionary<string, string> { [instance.Requested.StableId] = selected.StableId },
            cancellationToken: TestContext.Current.CancellationToken);
        var chosen = Assert.Single(preview.Resolution.Instances);
        Assert.Equal(PresetInstanceResolutionStatus.Resolved, chosen.Status);
        Assert.Equal(selected.StableId, Assert.Single(chosen.Candidates).StableId);
        Assert.Empty(preview.Plan.Errors);
        Assert.Contains(preview.Plan.Changes, change => change.ValueId == "inventory/1/quantity");
        Assert.DoesNotContain(preview.Plan.Changes, change => change.ValueId == "inventory/0/quantity");
        Assert.True(fixture.Session.ApplyPresetPlan(preview.PlanId!, preview.Revision,
            cancellationToken: TestContext.Current.CancellationToken).Ok);
        var changed = fixture.Bytes();
        Assert.NotEqual(fixture.Original, changed);
        Assert.True(fixture.Session.Undo().Ok);
        Assert.Equal(fixture.Original, fixture.Bytes());
        Assert.True(fixture.Session.Redo().Ok);
        Assert.Equal(changed, fixture.Bytes());
    }

    [Fact]
    public void ResolvedTargetsExposeOneCurrentCandidateWithoutAChoice()
    {
        var fixture = new SemanticPresetFixture();
        fixture.Materia(0, 0);
        fixture.Materia(1, 1, instance: SemanticPresetFixture.Instance + 1);
        var adapter = (IPresetDocument)fixture.Open();
        var allCopies = adapter.CapturePreset(new(MateriaPlanning.ActionId, new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("10020"),
            ["ap"] = JsonSerializer.SerializeToElement(20),
        },
            PresetScopeKinds.AllCopies, null, true));
        var allCopiesResolution = adapter.ResolvePresetInstances(allCopies.Instances);
        Assert.Equal(PresetInstanceResolutionStatus.Resolved, Assert.Single(allCopiesResolution.Instances).Status);
        var allCopiesCandidate = Assert.Single(allCopiesResolution.Instances[0].Candidates);
        Assert.StartsWith("ff7:materia-type/10020/all-copies/", allCopiesCandidate.StableId, StringComparison.Ordinal);

        var character = adapter.CapturePreset(new(CharacterPlanning.RecoveryActionId,
            new Dictionary<string, JsonElement>(), PresetScopeKinds.Selected, ["character/0"]));
        var characterResolution = adapter.ResolvePresetInstances(character.Instances);
        Assert.Equal(PresetInstanceResolutionStatus.Resolved, Assert.Single(characterResolution.Instances).Status);
        Assert.Single(characterResolution.Instances[0].Candidates);
    }

    [Fact]
    public void ExplicitEmptySelectedScopeRemainsEmptyAndDoesNotWiden()
    {
        using var fixture = new BulkSelectionFixture("PPSA08664", CharacterPlanning.RecoveryActionId);
        var adapter = (IPresetDocument)fixture.Document;
        var action = adapter.CapturePreset(new(CharacterPlanning.RecoveryActionId,
            new Dictionary<string, JsonElement>(), PresetScopeKinds.Selected, []));
        Assert.Equal(PresetScopeKinds.Selected, action.ScopeKind);
        Assert.Empty(action.Instances);
        var resolution = adapter.ResolvePresetInstances(action.Instances);
        var plan = adapter.PlanPreset([action], resolution);
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Changes);
    }

    [Fact]
    public void RemakeMateriaUsesTypeWideAllCopiesAndRejectsUnprovenSelectedIdentity()
    {
        using var fixture = new BulkSelectionFixture("PPSA01911", MateriaPlanning.StarActionId);
        var adapter = (IPresetDocument)fixture.Document;
        var parameters = new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement(fixture.TypeText),
            ["stars"] = JsonSerializer.SerializeToElement(2),
        };
        var allCopies = adapter.CapturePreset(new(MateriaPlanning.StarActionId,
            parameters, PresetScopeKinds.AllCopies, null, true));
        var instance = Assert.Single(allCopies.Instances);
        Assert.Equal("ff7.materia-type.all-copies", instance.Kind);
        var resolution = adapter.ResolvePresetInstances(allCopies.Instances);
        Assert.True(resolution.Succeeded);
        Assert.Empty(adapter.PlanPreset([allCopies], resolution).Errors);
        Assert.Throws<ArgumentException>(() => adapter.CapturePreset(new(MateriaPlanning.StarActionId,
            parameters, PresetScopeKinds.Selected, [fixture.Key(0)])));
    }

    [Fact]
    public void RebirthMateriaIdentitySurvivesPhysicalSlotMovementAndRejectsMissingDetail()
    {
        var source = new SemanticPresetFixture();
        source.Materia(0, 0);
        var sourceAdapter = (IPresetDocument)source.Open();
        var action = sourceAdapter.CapturePreset(new(MateriaPlanning.StarActionId,
            new Dictionary<string, JsonElement>
            {
                ["type"] = JsonSerializer.SerializeToElement("10020"),
                ["stars"] = JsonSerializer.SerializeToElement(2),
            }, PresetScopeKinds.Selected, ["materia/0"]));

        var moved = new SemanticPresetFixture();
        moved.Materia(7, 7);
        var movedAdapter = (IPresetDocument)moved.Open();
        var resolved = movedAdapter.ResolvePresetInstances(action.Instances);
        Assert.True(resolved.Succeeded);
        var plan = movedAdapter.PlanPreset([action], resolved);
        Assert.Contains(plan.Changes, change => change.ValueId.StartsWith("materia/7/", StringComparison.Ordinal));

        var missing = new SemanticPresetFixture();
        missing.Materia(0, 0);
        missing.Write(OwnedMateriaTargets.DetailOffset, uint.MaxValue);
        var missingAdapter = (IPresetDocument)missing.Open();
        var unresolved = missingAdapter.ResolvePresetInstances(action.Instances);
        Assert.Equal(PresetInstanceResolutionStatus.Unsupported, Assert.Single(unresolved.Instances).Status);
        Assert.False(unresolved.Succeeded);
    }

    [Fact]
    public void RetailAndDemoPublishDistinctCompatibilityVariants()
    {
        using var retail = new BulkSelectionFixture("PPSA08664", InventoryPlanning.ActionId);
        using var demo = new BulkSelectionFixture("PPSA18936", InventoryPlanning.ActionId);
        var retailCapability = ((IPresetDocument)retail.Document).PresetCapability;
        var demoCapability = ((IPresetDocument)demo.Document).PresetCapability;
        Assert.Equal("rebirth", retailCapability.FormatVariant);
        Assert.Equal("rebirth-demo", demoCapability.FormatVariant);
        Assert.NotEqual(retailCapability.SchemaFingerprint, demoCapability.SchemaFingerprint);
    }

    private static void ArrangeCharacters(byte[] bytes, Ff7Schema schema)
    {
        for (var character = 0; character < Ff7Schema.CharacterCount; character++)
        {
            var offset = CharacterLayout.Offset(schema, character);
            bytes[offset] = 40;
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + CharacterLayout.ExperienceDelta), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + CharacterLayout.HpDelta), character == 0 ? 1U : 2U);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + CharacterLayout.HpDelta + CharacterLayout.MaximumVitalDelta), 500);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + CharacterLayout.MpDelta), character == 0 ? 1U : 2U);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + CharacterLayout.MpDelta + CharacterLayout.MaximumVitalDelta), 50);
        }
    }
}
