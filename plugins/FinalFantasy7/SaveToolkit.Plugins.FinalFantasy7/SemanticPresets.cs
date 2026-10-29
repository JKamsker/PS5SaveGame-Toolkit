using System.Collections.ObjectModel;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy7;

internal sealed record PresetPreview(PresetResolution Resolution, ActionPlan Plan);

/// <summary>Plugin-owned capture/resolution only. Existing F6 planners remain the writer and preview authority.</summary>
internal sealed class SemanticPresets(DocumentPlanning planning, PresetSelectors selectors, bool readOnly,
    string titleId, Ff7Variant variant)
{
    public IReadOnlyList<ActionNode> Actions => readOnly ? [] :
        [planning.InventoryView(), .. planning.MateriaViews().Cast<ActionNode>(), .. planning.CharacterViews()];

    public Ff7ActionPreset Capture(string actionId, IReadOnlyDictionary<string, FieldValue> parameters,
        PresetScopeKind scopeKind, IReadOnlyList<string>? selectedRows = null, bool allMatches = false)
    {
        ValidateAction(actionId, parameters);
        if (scopeKind == PresetScopeKind.Selected ? selectedRows is null : selectedRows is not null || allMatches)
            throw new ArgumentException("Selected scope needs explicit rows; other scopes cannot carry historical rows or choices.");
        uint? materiaType = PresetSelectors.IsMateria(actionId) ? uint.Parse(parameters["type"].AsString(),
            System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture) : null;
        var targets = scopeKind == PresetScopeKind.Selected ? selectors.Capture(actionId, selectedRows!, allMatches, materiaType) : [];
        var preset = new Ff7ActionPreset(actionId,
            new ReadOnlyDictionary<string, FieldValue>(parameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)),
            scopeKind, targets, titleId, variant);
        _ = selectors.Resolve(preset); // Reject unsupported scope/domain combinations at capture too.
        return preset;
    }

    public PresetResolution Resolve(Ff7ActionPreset preset, IReadOnlyDictionary<int, string>? choices = null)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(preset.Targets);
        if (preset.TitleId != titleId || preset.Variant != variant)
            throw new ArgumentException("Preset title and schema variant must match exactly; retail and demo are distinct.");
        ValidateAction(preset.ActionId, preset.Parameters);
        return selectors.Resolve(preset, choices);
    }

    public PresetTargetResolution ResolveTarget(PresetTarget target, string? choice = null) =>
        selectors.ResolveTarget(target, choice);

    public PresetPreview Preview(Ff7ActionPreset preset, IReadOnlyDictionary<int, string>? choices = null)
    {
        var resolution = Resolve(preset, choices);
        return new(resolution, resolution.NeedsChoice
            ? new() { Errors = [new("preset", "Choose each ambiguous semantic target before previewing.")] }
            : planning.Plan(preset.ActionId, preset.Parameters, resolution.Scope));
    }

    private void ValidateAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (readOnly) throw new ArgumentException("Common/system files have no mutating presets.");
        if (!Actions.Any(action => action.ActionId == actionId)) throw new ArgumentException("Unsupported FFVII preset action.");
        var validation = planning.Plan(actionId, parameters, new([]));
        if (validation.Errors.Count != 0)
            throw new ArgumentException(string.Join(" ", validation.Errors.Select(error => error.Message)));
    }
}
