using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Sdk.Collections;

namespace SaveToolkit.Plugins.AstroPlayroom;

internal sealed class AstroDocument : BoundSaveDocument, IActionPlanningDocument, IQueryableDocument
{
    private const int PageSize = 64;
    private const string EditWarning = AstroWriteWarnings.Experimental + " " + AstroWriteWarnings.Integrity;
    private readonly string _path;
    private readonly AstroAllocation _allocation;
    private readonly IReadOnlyList<AstroField> _fields;
    private readonly Dictionary<string, AstroField> _byId;
    private readonly Dictionary<string, string> _replacements = new(StringComparer.Ordinal);
    private readonly Dictionary<string, AstroElement> _elements = new(StringComparer.Ordinal);
    private readonly AstroSchema _schema;
    private readonly AstroCollectionBrowser _collections;
    private readonly AstroReferences _references;
    private readonly AstroLocations _locations;
    private readonly AstroSpatialViews _spatial;

    public AstroDocument(string path, AstroAllocation allocation, IReferenceContext references)
    {
        _path = path;
        _allocation = allocation;
        _fields = AstroFields.Create(allocation);
        _byId = _fields.ToDictionary(f => f.Token.Id, StringComparer.Ordinal);
        _schema = new AstroSchema(allocation);
        _schema.Inventory(_fields);
        _references = new AstroReferences(references, _schema.Fingerprint);
        _locations = new AstroLocations(_schema, _references);
        _spatial = new AstroSpatialViews(_schema, _fields, _references, _locations, Current);
        _collections = new AstroCollectionBrowser(_schema, _locations, Current, ValueNode, CatalogueLabel,
            CatalogueReference,
            () => AstroFields.Coins(_fields) is { } coins
                ? Current(coins).AsInt64() : null);
        Register(allocation.Root);
        foreach (var field in _fields)
        {
            _ = BindValue(field);
        }
    }

    public override string Title => "ASTRO's PLAYROOM";

    public override AssetContent? GetAsset(string assetId) => _references.Asset(assetId);

    public IReadOnlyList<QueryableCollection> Collections => _collections.Queryable.Collections;

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) =>
        _collections.Queryable.Query(collectionId, query);

    public override ViewNode BuildView() => new TabsNode([
        new Tab("Overview", [
            new StatsNode(_fields.Where(f => f.Group == "Common" && f.Editable)
                .Select(f => new Stat(f.Label, Current(f).ToString())).ToArray()),
            new TextNode(EditWarning) { Tone = Tone.Warning },
            .. _fields.Where(f => f.Group == "Common" && f.Editable).Select(ValueNode),
            new TextNode(_fields.Any(f => f.Element.Name.StartsWith("Armadillo", StringComparison.Ordinal))
                ? "Special-bot fields are available read-only in Raw; their effects have not been validated."
                : "This save has no known special-bot unlock fields.")
        ]),
        new Tab("Progress", [AstroProgressSummary.Build(_fields, _references, Current), Table("Progress", "World")]),
        new Tab("Collectibles", _collections.Build()),
        new Tab("Maps", _spatial.Build()),
        new Tab("Schema", [_schema.Report()]),
        new Tab("Raw", [
            new TextNode($"Opaque bytes after the root: {_allocation.Terminator - _allocation.RootEnd}. Preserved on export."),
            SectionNode.Lazy("PreInstall tree", _allocation.Root.Id, $"{_fields.Count} values")])
    ]);

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        if (lazyId.StartsWith(AstroSpatialViews.LazyPrefix, StringComparison.Ordinal)) return _spatial.Expand(lazyId);
        // Page the immediate children as well as the depth: even hostile wide trees stay bounded.
        var split = lazyId.LastIndexOf('|');
        var id = split < 0 ? lazyId : lazyId[..split];
        var page = 0;
        if ((split >= 0 && (!int.TryParse(lazyId.AsSpan(split + 1), out page) || page < 0)) ||
            !_elements.TryGetValue(id, out var element))
        {
            throw new KeyNotFoundException($"Unknown ASTRO tree node '{lazyId}'.");
        }

        var count = element.Values.Count + element.Children.Count;
        if (page > count / PageSize)
        {
            throw new KeyNotFoundException("Unknown ASTRO tree page.");
        }

        var start = page * PageSize;
        var end = Math.Min(start + PageSize, count);
        var result = new List<ViewNode>(end - start + 1);
        for (var index = start; index < end; index++)
        {
            if (index < element.Values.Count)
            {
                result.Add(ValueNode(_byId[element.Values[index].Id]));
            }
            else
            {
                var child = element.Children[index - element.Values.Count];
                result.Add(SectionNode.Lazy(child.Name, child.Id));
            }
        }
        if ((page + 1) * PageSize < count)
        {
            result.Add(SectionNode.Lazy("More…", $"{id}|{page + 1}"));
        }

        return result;
    }

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
        AstroPlanning.Plan(actionId, parameters, scope, _schema, _fields, Current, _locations);

    public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
        _references.Handles(actionId) ? _references.Inspect(actionId, parameters) : base.InvokeAction(actionId, parameters);

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters,
        ActionScope scope, ActionPlan plan) =>
        AstroPlanning.Apply(actionId, parameters, scope, plan, _schema, _fields, Current, _locations, ApplyEdits);

    public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        var proposed = new Dictionary<string, string>(_replacements, StringComparer.Ordinal);
        foreach (var edit in edits)
        {
            if (!_byId.TryGetValue(edit.ValueId, out var field) || !field.Editable)
            {
                return EditOutcome.Rejected(edit.ValueId, "Unknown or read-only ASTRO field.");
            }

            if (!Valid(field, edit.Value))
            {
                return EditOutcome.Rejected(edit.ValueId, "Value does not fit the ASTRO field's type or storage range.");
            }

            proposed[edit.ValueId] = edit.Value == field.Initial ? _allocation.Read(field.Token) : edit.Value.ToString();
        }

        var size = _allocation.Terminator + proposed.Sum(p => Encoding.UTF8.GetByteCount(p.Value) - _byId[p.Key].Token.Length);
        if (size >= AstroAllocation.Size)
        {
            return EditOutcome.Rejected(edits.Count > 0 ? edits[^1].ValueId : "", "Edited text and terminator exceed the fixed 1 MiB allocation.");
        }

        var outcome = base.ApplyEdits(edits);
        // F3 status filters, section totals and details are view metadata. Rebuild on edits and
        // value-history replay so none of these snapshots can disagree with the live raw flags.
        return outcome with
        {
            StructureChanged = outcome.Succeeded && outcome.Changes.Any(c => c.OldValue != c.NewValue),
            Warnings = outcome.Succeeded && edits.Count > 0 ? [EditWarning] : outcome.Warnings
        };
    }

    public override IReadOnlyList<FileOutput> Serialize()
    {
        var output = new byte[AstroAllocation.Size];
        var source = 0;
        var target = 0;
        foreach (var field in _fields.Where(f => _replacements.ContainsKey(f.Token.Id)).OrderBy(f => f.Token.Offset))
        {
            var token = field.Token;
            var count = token.Offset - source;
            _allocation.Bytes.AsSpan(source, count).CopyTo(output.AsSpan(target));
            target += count;
            target += Encoding.UTF8.GetBytes(_replacements[token.Id], output.AsSpan(target));
            source = token.Offset + token.Length;
        }

        var remaining = _allocation.Terminator + 1 - source;
        if (target + remaining > output.Length)
        {
            throw new SaveFormatException("Edited ASTRO text exceeds its allocation.");
        }

        _allocation.Bytes.AsSpan(source, remaining).CopyTo(output.AsSpan(target));
        var parsed = AstroAllocation.Parse(output);
        var roundTrip = AstroFields.Create(parsed).ToDictionary(f => f.Token.Id, f => f.Initial, StringComparer.Ordinal);
        if (roundTrip.Count != _fields.Count || _fields.Any(f => !roundTrip.TryGetValue(f.Token.Id, out var value) || value != Current(f)))
        {
            throw new SaveFormatException("Edited ASTRO values did not survive serialization.");
        }

        return [new FileOutput(_path, output)];
    }

    // The SDK returns a node when registering a binding. Use only existing strings here;
    // presentation metadata is needed only when the field is actually shown.
    private FieldNode BindValue(AstroField field) => Field(field.Token.Id, field.Element.Name, field.Spec,
        () => Current(field), field.Editable ? value => Set(field, value) : null);

    private FieldNode ValueNode(AstroField field) => BindValue(field) with
    {
        Label = field.Label,
        Confidence = field.Confidence,
        Description = field.Path + (field.Token.IsAttribute ? $"/@{field.Token.Name}" : ""),
        Warning = field.Editable ? EditWarning : null
    };

    private FieldValue Current(AstroField field)
    {
        if (!_replacements.TryGetValue(field.Token.Id, out var text))
        {
            return field.Initial;
        }

        return field.Spec is BoolSpec ? FieldValue.Of(text == "true") :
            FieldValue.Of(long.Parse(text, System.Globalization.CultureInfo.InvariantCulture));
    }

    private void Set(AstroField field, FieldValue value)
    {
        if (value == field.Initial)
        {
            _replacements.Remove(field.Token.Id);
        }
        else
        {
            _replacements[field.Token.Id] = value.ToString();
        }
    }

    private static bool Valid(AstroField field, FieldValue value) => field.Spec switch
    {
        BoolSpec => value.Kind == FieldValueKind.Bool,
        IntegerSpec spec => value.Kind == FieldValueKind.Integer && value.AsInteger() >= spec.Min && value.AsInteger() <= spec.Max,
        _ => false
    };

    private FieldNode CatalogueLabel(string id, string label, string value, string description, Confidence confidence) =>
        Field(id, label, new TextSpec(), () => FieldValue.Of(value)) with
        { Description = description, Confidence = confidence };

    private FieldNode CatalogueReference(string id, string label, string value, string description, Confidence confidence) =>
        Field(id, label, new ReferenceSpec(AstroReferences.Collectibles), () => FieldValue.Of(value)) with
        { Description = description, Confidence = confidence };

    private TableNode Table(params string[] groups) => new([
        new TableColumn("path", "Saved name / path") { Width = ColumnWidth.Wide }, new TableColumn("value", "Value"),
        new TableColumn("alias", "Candidate location")
    ], _fields.Where(f => f.Editable && groups.Contains(f.Group, StringComparer.Ordinal))
        .Select(f => new TableRow(f.Token.Id, new Dictionary<string, ViewNode>(StringComparer.Ordinal)
        {
            ["path"] = new TextNode(f.Path["PreInstall/".Length..]), ["value"] = ValueNode(f),
            ["alias"] = WorldLabel(f)
        })).ToArray()) { Title = string.Join(" / ", groups) };

    private ViewNode WorldLabel(AstroField field)
    {
        var path = field.Path["PreInstall/".Length..];
        var parent = path[..path.LastIndexOf('/')];
        var reference = _references.World(parent);
        return reference is null ? new TextNode("Unmapped") : CatalogueLabel($"world-label:{field.Token.Id}",
            "Candidate location", reference.Name, $"{parent}. {reference.Description} {reference.AttributionText}", Confidence.Inferred);
    }

    private void Register(AstroElement element)
    {
        _elements.Add(element.Id, element);
        foreach (var child in element.Children)
        {
            Register(child);
        }
    }
}
