using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson;

internal sealed class JsonSemanticActions
{
    internal const string Prefix = "semantic/";
    internal const string Warning = JsonWriteWarnings.ConsoleRisk + " External integrity and game dependencies are unknown. "
        + "Use a spare save; owner validation is tracked in https://github.com/JKamsker/PS5SaveGame-Toolkit/issues/31.";
    internal const string DefaultsId = "semantic/defaults";
    internal const string FlagsId = "semantic/present-flags";
    internal const string CountersId = "semantic/reset-counters";
    internal const string ProgressId = "semantic/progress";
    internal const string WrapperId = "semantic/wrapper-imported";
    private readonly List<JsonSemanticTarget> _targets = [];
    private readonly Dictionary<int, JsonSemanticTarget> _byId = [];
    private readonly List<string> _unsupported = [];
    private readonly string? _title;
    private readonly bool _truncated;
    public JsonOverlay? Overlay { get; }
    public bool IsMatched => _title is not null;

    public JsonSemanticActions(ISaveFile file, JsonTokenTree tree, JsonReferenceChoices? references = null)
    {
        var overlay = Overlay = JsonOverlay.Match(file, tree);
        if (overlay is null) return;
        _title = file.Directory.TitleId;
        if (_title == "PPSA04027")
        {
            var options = overlay.Groups.SelectMany(g => JsonOverlay.Unique(g, "options")!.Children).Take(JsonResetActions.MaxChanges + 1).ToArray();
            _truncated = options.Length > JsonResetActions.MaxChanges;
            foreach (var option in options.Take(JsonResetActions.MaxChanges))
                if (JsonCyberpunkWrites.Create(option, references) is { } target) _targets.Add(target);
                else if (JsonOverlay.Unique(option, "value") is { } value) _unsupported.Add(value.ValueId);
        }
        else
        {
            var nodes = (overlay.IsProfile ? overlay.Groups : overlay.Groups.SelectMany(Leaves)).Take(JsonResetActions.MaxChanges + 1).ToArray();
            _truncated = nodes.Length > JsonResetActions.MaxChanges;
            foreach (var node in nodes.Take(JsonResetActions.MaxChanges))
                if (node.Parent is not null && node.UniqueMember
                    && JsonSemanticValues.Primitive(node, stringAdapters: true, profileCounters: overlay.IsProfile) is { } target)
                    _targets.Add(overlay.IsProfile ? target : target with { Confidence = Confidence.Unknown });
                else _unsupported.Add(node.ValueId);
        }
        foreach (var target in _targets) _byId.Add(target.Node.Index, target);
    }

    public IReadOnlyList<ViewNode> ForNode(JsonValueToken node)
    {
        var target = _byId.GetValueOrDefault(node.Index);
        if (target is null) return [];
        var result = new List<ViewNode> { target.View() };
        if (target.Spec is ReferenceSpec spec)
            result.Add(new FieldNode("catalogue/" + node.Index, "Browse choice catalogue (imported ID)", spec with { ReadOnly = true }, FieldValue.Of(node.Original.ToString()))
                { Confidence = target.Confidence, Description = "Anchored to the imported ID; the current stored value is shown above. " + target.Evidence });
        if (target.Defaults is not null) result.Add(Action($"semantic/{node.Index}/default", "Restore recorded default"));
        return result;
    }

    public IReadOnlyList<ViewNode> Presets() => _title switch
    {
        "PPSA04027" => [Action(DefaultsId, "Restore recorded setting defaults")],
        "PPSA02343" => [Action(FlagsId, "Set present minigame flags", [new("enabled", "Stored flag value", new BoolSpec(), FieldValue.Of(true))
            { Confidence = Confidence.Inferred, Description = JsonOverlay.Provenance + " " + Warning, Warning = Warning }]),
            Action(CountersId, "Reset present minigame counters to zero"), ProgressAction()],
        "PPSA07696" => [Action(WrapperId, "Restore imported wrapper configuration")],
        _ => [],
    };

    public ActionPlan Plan(JsonTokenTree tree, string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        try
        {
            if (_title is null) throw new ArgumentException("No supported title/role/schema matches this action.");
            if (scope.QueryScope is { } query && (!JsonSearch.ValidScope(tree, query.CollectionId) || scope.RowKeys is null))
                throw new ArgumentException("Use a resolved JSON tree selection.");
            var selected = scope.RowKeys?.ToHashSet(StringComparer.Ordinal);
            if (_truncated && selected is null) throw new ArgumentException("Title adapter coverage exceeds 5,000 fields; use an explicit bounded selection.");
            if (selected is not null && (selected.Count != scope.RowKeys!.Count || selected.Count > JsonResetActions.MaxChanges || selected.Any(id => tree.FindValue(id) is null)))
                throw new ArgumentException("Selection includes missing nodes or exceeds the action bound.");
            var changes = new Dictionary<string, FieldValue>(StringComparer.Ordinal);
            var skipped = new List<SkippedActionRow>();
            var eligible = _targets.Where(t => selected is null || selected.Contains(t.Node.ValueId)).ToArray();
            if (actionId is DefaultsId or FlagsId or CountersId or WrapperId)
            {
                if (actionId == DefaultsId && _title != "PPSA04027" || actionId == WrapperId && _title != "PPSA07696"
                    || actionId is FlagsId or CountersId && _title != "PPSA02343") throw new ArgumentException("Preset does not match this title.");
                foreach (var target in eligible)
                {
                    IReadOnlyDictionary<string, FieldValue>? values = actionId switch
                    {
                        DefaultsId => target.Defaults,
                        WrapperId => new Dictionary<string, FieldValue> { [target.Node.ValueId] = target.Node.Original },
                        FlagsId when target.IsFlag => target.Changes(new Dictionary<string, FieldValue> { ["value"] = Boolean(parameters, "enabled") }),
                        CountersId when target.IsCounter => target.Changes(new Dictionary<string, FieldValue> { ["value"] = FieldValue.Of(0L) }),
                        _ => null,
                    };
                    if (values is null) skipped.Add(new(target.Node.ValueId, "Not eligible for this preset or no valid recorded default; left unchanged."));
                    else foreach (var pair in values) changes.Add(pair.Key, pair.Value);
                }
                if (selected is not null)
                    foreach (var id in selected.Except(eligible.Select(t => t.Node.ValueId))) skipped.Add(new(id, "Not an eligible title gameplay/settings field; unchanged."));
                else foreach (var id in _unsupported) skipped.Add(new(id, "Unsupported or ambiguous adapter metadata; unchanged. Generic JSON access remains available."));
            }
            else if (actionId == ProgressId)
            {
                if (_title != "PPSA02343" || selected is not null) throw new ArgumentException("Progress tuple requires It Takes Two document scope.");
                foreach (var key in ProgressKeys)
                {
                    var target = _targets.SingleOrDefault(t => t.Node.Label == key) ?? throw new ArgumentException("Progress tuple requires all three existing, unambiguous fields.");
                    if (!parameters.TryGetValue(key, out var value) || target.Validate(value) is { }) throw new ArgumentException("Invalid bounded chapter/checkpoint string.");
                    foreach (var pair in target.Changes(new Dictionary<string, FieldValue> { ["value"] = value })) changes.Add(pair.Key, pair.Value);
                }
            }
            else
            {
                var parts = actionId.Split('/');
                if (parts.Length != 3 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)) throw new ArgumentException("Unknown semantic action.");
                var target = _byId.GetValueOrDefault(id) ?? throw new ArgumentException("Field no longer has a supported semantic adapter.");
                if (selected is not null) throw new ArgumentException("A single-field adapter requires document scope.");
                var values = parts[2] == "set" ? target.Changes(parameters) : parts[2] == "default" && target.Defaults is { } defaults
                    ? defaults : throw new ArgumentException("No supported recorded default/action.");
                foreach (var pair in values) changes.Add(pair.Key, pair.Value);
            }
            var planned = changes.Where(p => tree.FindValue(p.Key)!.Current != p.Value).Select(p =>
            {
                var node = tree.FindValue(p.Key)!;
                return new PlannedChange(p.Key, JsonScalar.ReviewTransition(JsonPaths.ForNode(node), node.Current, p.Value), node.Current, p.Value)
                    { Derived = node.Label == "index" };
            }).ToArray();
            if (planned.Length + skipped.Count > JsonResetActions.MaxChanges)
                throw new ArgumentException("Preview exceeds 5,000 changed/skipped fields; narrow the selection.");
            if (planned.Length > 0)
            {
                var trial = tree.Clone();
                foreach (var change in planned)
                {
                    var node = trial.FindValue(change.ValueId)!;
                    if (node.Validate(change.NewValue) is { } error) throw new ArgumentException(error);
                    node.Current = change.NewValue;
                }
                _ = JsonTreeWriter.Write(trial);
            }
            return new() { Changes = planned, SkippedRows = skipped, Warnings = [Warning,
                "Only existing values are edited. Missing keys, story dependencies and companions are not synthesized. Numeric limits may be conservative observed bounds, not game caps.",
                _title switch
                {
                    "PPSA04027" => "Value/index authority is unconfirmed. Float steps are approximate/advisory; integer steps allow recorded endpoints. Recorded defaults are not console-verified defaults.",
                    "PPSA02343" => "Flag effects, counter units and chapter/checkpoint dependencies are unconfirmed. Changing present flags does not create missing minigame/story state.",
                    _ => "Wrapper numeric ranges run only between zero and the imported value. Zero-valued unknown enums have a single-value range. No PS1 preset meanings or DATA.BIN edits are inferred.",
                }] };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException or SaveFormatException)
        { return new() { Errors = [new(actionId, ex.Message)], Warnings = [Warning] }; }
    }

    private static readonly string[] ProgressKeys = ["FurthestUnlockedChapter", "LastSaveChapter", "LastSaveProgressPoint"];
    private ActionNode ProgressAction() => Action(ProgressId, "Set existing chapter/checkpoint tuple (unverified)",
        ProgressKeys.Select(key => _targets.Find(t => t.Node.Label == key)?.Parameter(key)
            ?? new FieldNode(key, key + " (missing; action will reject)", new TextSpec { MaxLength = 0 }, FieldValue.Of(""))
                { Confidence = Confidence.Unknown, Warning = Warning, Description = Warning }).ToArray());

    private static ActionNode Action(string id, string label, IReadOnlyList<FieldNode>? parameters = null) => new(id, label)
    { SupportsPlanning = true, Parameters = parameters ?? [], Description = JsonOverlay.Provenance + " " + Warning };

    private static FieldValue Boolean(IReadOnlyDictionary<string, FieldValue> parameters, string key) =>
        parameters.TryGetValue(key, out var value) && value.Kind == FieldValueKind.Bool ? value : throw new ArgumentException("Supply a boolean flag value.");

    private static IEnumerable<JsonValueToken> Leaves(JsonValueToken node) => node.IsContainer ? node.Children.SelectMany(Leaves) : [node];
}
