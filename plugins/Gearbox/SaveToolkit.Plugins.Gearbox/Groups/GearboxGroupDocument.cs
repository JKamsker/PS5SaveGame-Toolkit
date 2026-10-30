using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

internal sealed class GearboxGroupDocument : IGroupedSaveDocument, IQueryableDocument, IActionEffectDocument,
    IGroupedFieldContext
{
    private readonly IReferenceContext _references;
    private readonly MemberBytes[] _original;
    private DocumentMemberAdapter[] _members;
    private DocumentMemberRouter _router;

    public GearboxGroupDocument(DocumentGroupInput input, IReferenceContext references)
    {
        _references = references;
        _original = input.Members.OrderBy(m => m.Role, StringComparer.Ordinal).Select(m => new MemberBytes(
            m.Role, m.File.RelativePath, m.File.Directory.TitleId, m.File.ReadAllBytes().ToArray())).ToArray();
        _members = OpenMembers(_original);
        _router = new(_members);
        try
        {
            // Keep the exact models that passed validation rather than reopening both members.
            foreach (var member in _members)
                GearboxGroups.ValidateRoundTrip(member.Document, _original.Single(m => m.Role == member.Role).Bytes);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public string Title => "Gearbox character + profile";
    public bool CanEdit => true;
    public IReadOnlyList<QueryableCollection> Collections => _members.SelectMany(m => m.Collections).ToArray();

    public ViewNode BuildView() => new SectionNode(Title,
    [
        new TextNode("These explicitly selected files share an imported generation. This does not prove gameplay compatibility or which Guardian/account mirror wins. Backups are separate. " + SerialWrites.Warning) { Tone = Tone.Warning },
        .. GearboxTransferActions.Build(),
        .. _members.Select(m => (ViewNode)new SectionNode(m.Role == GearboxGroups.ProfileRole ? "Shared profile / bank" : "Character progression / backpack",
            [m.Node(m.Document.BuildView())])),
    ]);

    public IReadOnlyList<ViewNode> ExpandNode(string lazyId) => _router.Resolve(lazyId).ExpandNode(lazyId);

    public FieldValue GetValue(string valueId) => _router.Resolve(valueId).GetValue(valueId);

    public IReadOnlyList<FieldMetadata> ResolveFieldMetadata(IReadOnlyList<string> valueIds)
    {
        ArgumentNullException.ThrowIfNull(valueIds);
        if (valueIds.Count > FieldMetadataLimits.MaxBatchSize)
            throw new ArgumentOutOfRangeException(nameof(valueIds), valueIds.Count,
                $"Request at most {FieldMetadataLimits.MaxBatchSize} field IDs per batch.");
        foreach (var id in valueIds) ArgumentException.ThrowIfNullOrEmpty(id, nameof(valueIds));

        // Unknown IDs follow the document contract and remain unavailable. A recognized identity
        // must have exactly one owner; the router still validates member results and request order.
        var owned = valueIds.Where(id =>
        {
            var matches = _members.Count(member => member.Owns(id));
            if (matches > 1) throw new InvalidOperationException("The group identity matches multiple member policies.");
            return matches == 1;
        }).ToArray();
        return owned.Length == 0 ? [] : _router.ResolveFieldMetadata(owned);
    }

    public IReadOnlyList<DocumentGroupFieldContext> ResolveFieldContext(IReadOnlyList<string> valueIds)
    {
        ArgumentNullException.ThrowIfNull(valueIds);
        if (valueIds.Count > FieldMetadataLimits.MaxBatchSize)
            throw new ArgumentOutOfRangeException(nameof(valueIds), valueIds.Count,
                $"Request at most {FieldMetadataLimits.MaxBatchSize} field IDs per batch.");
        foreach (var id in valueIds) ArgumentException.ThrowIfNullOrEmpty(id, nameof(valueIds));

        var owners = new Dictionary<string, DocumentMemberAdapter>(StringComparer.Ordinal);
        var ownedIds = new List<string>(valueIds.Count);
        foreach (var id in valueIds.Distinct(StringComparer.Ordinal))
        {
            try
            {
                owners.Add(id, _router.Resolve(id));
                ownedIds.Add(id);
            }
            catch (KeyNotFoundException) { }
        }

        if (owners.Count == 0) return [];
        var metadata = _router.ResolveFieldMetadata(ownedIds);
        var contexts = new List<DocumentGroupFieldContext>(metadata.Count);
        foreach (var field in metadata)
        {
            if (!owners.TryGetValue(field.ValueId, out var owner)) continue;
            var selected = _original.Single(member => member.Role == owner.Role);
            contexts.Add(new DocumentGroupFieldContext(field.ValueId, owner.Role, selected.Path));
        }
        return contexts;
    }

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) => _router.Resolve(collectionId).Query(collectionId, query);

    public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        var before = CaptureMemento();
        var changes = new List<ValueChange>();
        try
        {
            foreach (DocumentMemberEdits batch in _router.PartitionEdits(edits))
            {
                // Staging replaces member documents before dispatch. Re-register every requested
                // live binding on that candidate so unloaded item/account fields never retain an
                // adapter or setter from the published model.
                if (batch.Member.Document is GearboxDocument member)
                {
                    var localIds = batch.Edits.Select(edit => batch.Member.Local(edit.ValueId))
                        .Distinct(StringComparer.Ordinal).ToArray();
                    foreach (var chunk in localIds.Chunk(FieldMetadataLimits.MaxBatchSize))
                        _ = member.ResolveFieldMetadata(chunk);
                }
                var outcome = batch.Member.ApplyEdits(batch.Edits);
                if (!outcome.Succeeded)
                {
                    _ = RestoreMemento(before);
                    return new() { Errors = outcome.Errors };
                }
                changes.AddRange(outcome.Changes);
            }
            _ = Serialize();
            return new() { Changes = changes, Revert = changes.Count > 0 ? before : null, StructureChanged = changes.Count > 0, Warnings = [SerialWrites.Warning] };
        }
        catch
        {
            _ = RestoreMemento(before);
            throw;
        }
    }

    public ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
        _router.Resolve(actionId).InvokeAction(actionId, parameters);

    internal GearboxDocument Character => (GearboxDocument)_members.Single(m => m.Role == GearboxGroups.CharacterRole).Document;
    internal GearboxDocument Profile => (GearboxDocument)_members.Single(m => m.Role == GearboxGroups.ProfileRole).Document;

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        if (actionId.StartsWith("transfer/", StringComparison.Ordinal)) return new GearboxTransferActions(this).Plan(actionId, scope);
        return _router.Resolve(actionId).PlanAction(actionId, parameters, scope);
    }

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
    {
        var validation = ActionPlans.Revalidate(plan, () => PlanAction(actionId, parameters, scope), new("", "The Gearbox group preview changed; preview again before applying."));
        var fresh = validation.Plan;
        if (!validation.Succeeded) return new() { Errors = validation.Errors, Warnings = fresh.Warnings };
        if (fresh.Changes.Count == 0) return new() { Warnings = fresh.Warnings };
        var before = CaptureMemento();
        try
        {
            if (actionId.StartsWith("transfer/", StringComparison.Ordinal)) new GearboxTransferActions(this).Apply(actionId, scope);
            else
            {
                var outcome = _router.Resolve(actionId).ApplyActionPlan(actionId, parameters, scope, fresh);
                if (!outcome.Succeeded) { _ = RestoreMemento(before); return outcome; }
            }
            _ = Serialize();
            return new() { StructureChanged = true, Revert = before, Warnings = fresh.Warnings,
                Changes = fresh.Changes.Select(c => new ValueChange(c.ValueId, c.OldValue, c.NewValue)).ToArray() };
        }
        catch { _ = RestoreMemento(before); throw; }
    }

    public IReadOnlyList<FileOutput> Serialize() => _members.SelectMany(m => m.Document.Serialize()).ToArray();
    public AssetContent? GetAsset(string assetId) => _references.GetAsset(assetId);
    public ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count) => _router.Resolve(blobId).ReadBlob(blobId, offset, count);

    public DocumentMemento CaptureMemento() => new GroupSnapshot(_original, _members.ToDictionary(m => m.Role, m => ((GearboxDocument)m.Document).CaptureState(), StringComparer.Ordinal));

    public DocumentMemento RestoreMemento(DocumentMemento memento)
    {
        if (memento is not GroupSnapshot snapshot || !snapshot.Matches(_original))
            throw new InvalidOperationException("The snapshot does not belong to this Gearbox input set.");
        var inverse = CaptureMemento();
        var restored = OpenMembers(snapshot.Copy());
        foreach (var member in restored) _ = member.Document.RestoreMemento(snapshot.States[member.Role]);
        foreach (var member in _members) member.Document.Dispose();
        _members = restored;
        _router = new(_members);
        return inverse;
    }

    private DocumentMemberAdapter[] OpenMembers(MemberBytes[] members) => members.Select(m =>
    {
        var container = GearboxContainer.Parse(m.Bytes.ToArray(), m.Title, m.Role == GearboxGroups.ProfileRole);
        return new DocumentMemberAdapter(m.Role, new GearboxDocument(m.Path, m.Title, container, container.Decode(), _references) { Coordinated = true },
            GearboxGroupIdentity.Policy);
    }).ToArray();

    public void Dispose()
    {
        foreach (var member in _members) member.Document.Dispose();
    }

    private sealed record MemberBytes(string Role, string Path, string Title, byte[] Bytes);

    private sealed class GroupSnapshot(MemberBytes[] members, Dictionary<string, DocumentMemento> states) : DocumentMemento
    {
        public IReadOnlyDictionary<string, DocumentMemento> States { get; } = states;
        private readonly MemberBytes[] _members = members.Select(m => m with { Bytes = m.Bytes.ToArray() }).ToArray();
        public override long SizeBytes => 128 + States.Values.Sum(s => s.SizeBytes) + _members.Sum(m => 128L + m.Bytes.LongLength + 2L * (m.Role.Length + m.Path.Length + m.Title.Length));
        public MemberBytes[] Copy() => _members.Select(m => m with { Bytes = m.Bytes.ToArray() }).ToArray();
        public bool Matches(MemberBytes[] other) => _members.Length == other.Length && _members.Zip(other).All(pair =>
            pair.First.Role == pair.Second.Role && pair.First.Path == pair.Second.Path && pair.First.Title == pair.Second.Title &&
            pair.First.Bytes.AsSpan().SequenceEqual(pair.Second.Bytes));
    }
}
