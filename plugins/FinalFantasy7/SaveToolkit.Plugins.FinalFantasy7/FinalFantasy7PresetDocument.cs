using System.Globalization;
using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SdkPresetResolution = SaveToolkit.Sdk.PresetResolution;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Thin SDK-07 adapter over the existing FFVII semantic selectors and F6 planners.</summary>
internal sealed class FinalFantasy7PresetDocument : IPresetDocument
{
    private readonly SemanticPresets _semantic;
    private readonly DocumentPlanning _planning;
    private readonly ReferenceData _references;
    private readonly Ff7Schema _schema;
    private readonly bool _readOnly;
    private readonly Func<ScalarField, FieldNode> _bind;
    private readonly Func<IReadOnlyList<ValueEdit>, EditOutcome> _apply;

    public FinalFantasy7PresetDocument(SemanticPresets semantic, DocumentPlanning planning, ReferenceData references, Ff7Schema schema,
        string titleId, bool readOnly, string? referenceIdentity, Func<ScalarField, FieldNode> bind,
        Func<IReadOnlyList<ValueEdit>, EditOutcome> apply)
    {
        _semantic = semantic;
        _planning = planning;
        _references = references;
        _schema = schema;
        _readOnly = readOnly;
        _bind = bind;
        _apply = apply;
        PresetCapability = FinalFantasy7PresetDescriptors.Create(schema, readOnly, referenceIdentity);
        TitleId = titleId;
    }

    public string TitleId { get; }
    public PresetCapability PresetCapability { get; }

    public PresetAction CapturePreset(PresetCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureAction(request.ActionId, request.ScopeKind);
        var parameters = ReadParameters(request.ActionId, request.Parameters);
        var selected = request.ScopeKind == PresetScopeKinds.Selected
            ? request.SelectedRowKeys ?? throw new ArgumentException("Selected scope requires an explicit row-key list.")
            : null;
        if (request.ScopeKind != PresetScopeKinds.Selected && request.SelectedRowKeys is not null)
            throw new ArgumentException("Only selected scope may carry row keys.");
        if (request.AllMatches && request.ScopeKind is not (PresetScopeKinds.AllEligible or PresetScopeKinds.AllCopies))
            throw new ArgumentException("All-matches capture is limited to all-eligible or all-copies scope.");

        var internalPreset = _semantic.Capture(request.ActionId, parameters, Scope(request.ScopeKind), selected);
        var instances = internalPreset.Targets.Select(target => FinalFantasy7PresetIdentity.ToReference(target, _references)).ToList();
        if (request.ScopeKind == PresetScopeKinds.AllCopies)
            instances.Add(FinalFantasy7PresetIdentity.ToReference(new(PresetIdentityDomain.MateriaType, ReadType(parameters), AllMatches: true), _references));
        return new(request.ActionId, request.Parameters.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal),
            instances, request.ScopeKind);
    }

    public SdkPresetResolution ResolvePresetInstances(IReadOnlyList<PresetInstanceReference> instances) =>
        ResolvePresetInstances(instances, new Dictionary<string, string>(StringComparer.Ordinal));

    public SdkPresetResolution ResolvePresetInstances(IReadOnlyList<PresetInstanceReference> instances,
        IReadOnlyDictionary<string, string> choices)
    {
        ArgumentNullException.ThrowIfNull(instances);
        ArgumentNullException.ThrowIfNull(choices);
        var results = new List<PresetInstanceResolution>(instances.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var requested in instances)
        {
            if (requested is null || string.IsNullOrEmpty(requested.StableId) ||
                !seen.Add(requested.StableId) || !FinalFantasy7PresetIdentity.TryTarget(requested, out var target))
            {
                results.Add(new(requested ?? new PresetInstanceReference("ff7.invalid", "ff7:invalid"),
                    PresetInstanceResolutionStatus.Unsupported, [], "foreign-or-duplicate-identity"));
                continue;
            }

            if (!FinalFantasy7PresetIdentity.HasStableMetadata(requested, target, _references))
            {
                results.Add(new(requested, PresetInstanceResolutionStatus.Unsupported, [], "tampered-identity-metadata"));
                continue;
            }

            var current = _semantic.ResolveTarget(target);
            var candidates = current.Candidates.Select(candidate => FinalFantasy7PresetIdentity.ToCandidate(target, candidate, _references)).ToArray();
            choices.TryGetValue(requested.StableId, out var choiceId);
            if (choiceId is not null)
            {
                if (target.AllMatches || current.Status != PresetTargetStatus.Ambiguous)
                {
                    results.Add(new(requested, PresetInstanceResolutionStatus.Unsupported, [],
                        "choice-is-not-valid-for-this-identity"));
                    continue;
                }
                var choice = current.Candidates.FirstOrDefault(candidate => FinalFantasy7PresetIdentity.ToCandidate(target, candidate, _references).StableId == choiceId);
                if (choice is null)
                {
                    results.Add(new(requested, PresetInstanceResolutionStatus.Missing, [], "choice-is-not-current"));
                    continue;
                }
                current = _semantic.ResolveTarget(target, choice.RowKey);
                candidates = [FinalFantasy7PresetIdentity.ToCandidate(target, choice, _references)];
            }

            var status = FinalFantasy7PresetIdentity.Status(current.Status);
            IReadOnlyList<PresetInstanceReference> presented = status == PresetInstanceResolutionStatus.Resolved
                ? target.AllMatches ? [FinalFantasy7PresetIdentity.ToAllCopiesCandidate(target, current.Candidates, _references)] : candidates
                : status == PresetInstanceResolutionStatus.Ambiguous ? candidates : [];
            results.Add(new(requested, status, presented, current.Reason));
        }
        return new(results);
    }

    public ActionPlan PlanPreset(IReadOnlyList<PresetAction> actions, SdkPresetResolution resolution)
    {
        try
        {
            if (_readOnly) return Error("preset", "Common files are read-only.");
            if (actions is not [{ } action] || !resolution.Succeeded)
                return Error("preset", "The preset must contain one fully resolved action.");
            EnsureAction(action.ActionId, action.ScopeKind);
            var parameters = ReadParameters(action.ActionId, action.Parameters);
            ValidateTargets(action, parameters);
            var fresh = ResolvePresetInstances(action.Instances ?? [], ExtractChoices(resolution));
            if (!FinalFantasy7PresetIdentity.Equivalent(resolution, fresh)) return Error("preset", "Preset targets changed; preview again.");
            var scope = BuildScope(action, resolution, parameters);
            return _planning.Plan(action.ActionId, parameters, scope);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            return Error("preset", exception.Message);
        }
    }

    public EditOutcome ApplyPreset(IReadOnlyList<PresetAction> actions, SdkPresetResolution resolution, ActionPlan plan)
    {
        try
        {
            if (_readOnly) return EditOutcome.Rejected("preset", "Common files are read-only.");
            if (actions is not [{ } action] || !resolution.Succeeded)
                return EditOutcome.Rejected("preset", "The preset must contain one fully resolved action.");
            EnsureAction(action.ActionId, action.ScopeKind);
            var parameters = ReadParameters(action.ActionId, action.Parameters);
            ValidateTargets(action, parameters);
            var fresh = ResolvePresetInstances(action.Instances ?? [], ExtractChoices(resolution));
            if (!FinalFantasy7PresetIdentity.Equivalent(resolution, fresh)) return EditOutcome.Rejected("preset", "Preset targets changed; preview again.");
            return _planning.Apply(action.ActionId, parameters, BuildScope(action, resolution, parameters), plan, _bind, _apply);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            return EditOutcome.Rejected("preset", exception.Message);
        }
    }

    private void EnsureAction(string actionId, string scope)
    {
        if (_readOnly || PresetCapability.Actions.All(action => action.ActionId != actionId || !action.SupportedScopes.Contains(scope)))
            throw new ArgumentException("Unsupported FFVII preset action or scope.");
    }

    private static void ValidateTargets(PresetAction action, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        var instances = action.Instances;
        if (action.ScopeKind == PresetScopeKinds.Selected && instances is null)
            throw new ArgumentException("Selected scope requires an explicit semantic target list.");
        var targets = (instances ?? []).Select(FinalFantasy7PresetIdentity.ParseTarget).ToArray();
        if (targets.DistinctBy(target => (target.Domain, target.Id, target.TypeId, target.AllMatches)).Count() != targets.Length)
            throw new ArgumentException("Duplicate semantic targets are not permitted.");
        if (action.ScopeKind is (PresetScopeKinds.Document or PresetScopeKinds.AllEligible) && targets.Length != 0)
            throw new ArgumentException("Document and all-eligible scopes cannot carry explicit targets.");
        if (action.ScopeKind == PresetScopeKinds.AllCopies &&
            (targets is not [{ Domain: PresetIdentityDomain.MateriaType, AllMatches: true }] ||
             targets[0].Id != ReadType(parameters)))
            throw new ArgumentException("All-copies scope requires one matching materia type identity.");
        if (action.ScopeKind != PresetScopeKinds.Selected) return;
        foreach (var target in targets)
        {
            var valid = action.ActionId == InventoryPlanning.ActionId
                ? target is { Domain: PresetIdentityDomain.ItemType, TypeId: null, AllMatches: false }
                : CharacterPlanning.Handles(action.ActionId)
                    ? target is { Domain: PresetIdentityDomain.CharacterIndex, TypeId: null, AllMatches: false }
                    : target is { Domain: PresetIdentityDomain.MateriaInstance, TypeId: { } type, AllMatches: false } &&
                        type == ReadType(parameters);
            if (!valid) throw new ArgumentException("Target identity does not belong to the action domain.");
        }
    }

    private static IReadOnlyDictionary<string, FieldValue> ReadParameters(string actionId, IReadOnlyDictionary<string, JsonElement> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var expected = actionId switch
        {
            InventoryPlanning.ActionId => new[] { "quantity" },
            MateriaPlanning.ActionId => new[] { "type", "ap" },
            MateriaPlanning.StarActionId => new[] { "type", "stars" },
            CharacterPlanning.RecoveryActionId => [],
            CharacterPlanning.LevelActionId => new[] { "level" },
            _ => throw new ArgumentException("Unsupported FFVII preset action."),
        };
        if (values.Keys.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(StringComparer.Ordinal)) == false)
            throw new ArgumentException("Preset parameters do not match the published action descriptor.");
        var result = new Dictionary<string, FieldValue>(StringComparer.Ordinal);
        foreach (var id in expected)
        {
            var value = values[id];
            result[id] = id == "type" ? FieldValue.Of(ReadCatalogType(value))
                : FieldValue.Of(ReadInteger(value, id));
        }
        return result;
    }

    private static long ReadInteger(JsonElement value, string id)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String && TryParseInteger(value.GetString(), out number)) return number;
        throw new ArgumentException($"Preset parameter '{id}' must be an invariant integer.");
    }

    private static string ReadCatalogType(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String)
            throw new ArgumentException("Materia type must be a canonical decimal catalog id.");
        var text = value.GetString();
        if (text is null || !uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var type)
            || !string.Equals(text, type.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            throw new ArgumentException("Materia type must be a canonical decimal catalog id.");
        return text;
    }

    private static bool TryParseInteger(string? text, out long value)
    {
        text = text?.Trim();
        if (text is null) { value = default; return false; }
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (ulong.TryParse(text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var hex)
                && hex <= long.MaxValue)
            {
                value = (long)hex;
                return true;
            }
            value = default;
            return false;
        }
        return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    private ActionScope BuildScope(PresetAction action, SdkPresetResolution resolution,
        IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (action.ScopeKind != PresetScopeKinds.Selected)
        {
            if (!PresetSelectors.IsMateria(action.ActionId) || _schema.IsRemake)
                return new();
            var internalPreset = new Ff7ActionPreset(action.ActionId, parameters,
                Scope(action.ScopeKind), [], TitleId, _schema.Variant);
            var current = _semantic.Resolve(internalPreset);
            return new(current.Scope.RowKeys?.ToArray() ?? []);
        }
        var rows = action.Instances!.Select(reference =>
        {
            var resolved = resolution.Instances.Single(item => item.Requested.StableId == reference.StableId);
            var target = FinalFantasy7PresetIdentity.ParseTarget(reference);
            var candidates = resolved.Candidates is { Count: 1 } presented
                ? presented
                : _semantic.ResolveTarget(target).Candidates.Select(candidate =>
                    FinalFantasy7PresetIdentity.ToCandidate(target, candidate, _references)).ToArray();
            return candidates.Select(candidate => FinalFantasy7PresetIdentity.CandidateRow(target, candidate));
        }).SelectMany(rows => rows).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new(rows);
    }

    private static PresetScopeKind Scope(string scope) => scope switch
    {
        PresetScopeKinds.Document => PresetScopeKind.Document,
        PresetScopeKinds.AllEligible => PresetScopeKind.AllEligible,
        PresetScopeKinds.AllCopies => PresetScopeKind.AllCopies,
        PresetScopeKinds.Selected => PresetScopeKind.Selected,
        _ => throw new ArgumentException("Unknown preset scope."),
    };

    private static IReadOnlyDictionary<string, string> ExtractChoices(SdkPresetResolution resolution) =>
        resolution.Instances.Where(item => item.Status == PresetInstanceResolutionStatus.Resolved &&
                item.Message == "explicit-choice" && item.Candidates.Count == 1)
            .ToDictionary(item => item.Requested.StableId, item => item.Candidates[0].StableId, StringComparer.Ordinal);

    private static ActionPlan Error(string field, string message) => new() { Errors = [new(field, message)] };

    private static uint ReadType(IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (!uint.TryParse(parameters["type"].AsString(), NumberStyles.None, CultureInfo.InvariantCulture, out var type))
            throw new ArgumentException("Materia type must be a canonical decimal catalog id.");
        return type;
    }
}
