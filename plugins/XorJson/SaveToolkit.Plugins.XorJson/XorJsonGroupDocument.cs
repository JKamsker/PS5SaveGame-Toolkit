using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.XorJson;

internal sealed class XorJsonGroupDocument : IGroupedSaveDocument, IQueryableDocument, IActionPlanningDocument
{
    private const string GroupWarning = "Experimental character/global transaction. No shared game dependency or console reload has been verified; every member is staged and exported together, but the game may reject or repair the result.";
    private const string UnknownMemberIdentity = "Unknown XorJson group member identity.";
    private readonly IReferenceContext _references;
    private readonly DocumentMemberAdapter[] _members;
    private readonly DocumentMemberRouter _router;
    private static readonly DocumentMemberIdentityPolicy MemberIdentity = new()
    {
        TableCellScope = static (scope, row, _) => scope + "/" + row,
        ExpansionScope = static (_, _) => "view",
        PrefixGraphIds = false,
        PrefixMapMarkerIds = false,
        CollectionColumn = static (document, column) => column with
        { Spec = column.Spec is { } spec ? spec with { ReadOnly = !document.CanEdit } : null },
        NavigationPath = static (role, path) => ["section:XorJson character + global", "section:" + role, .. path],
    };

    public XorJsonGroupDocument(XorJsonPlugin plugin, DocumentGroupInput input, IReferenceContext references)
    {
        _references = references;
        _members = input.Members.OrderBy(member => member.Role, StringComparer.Ordinal)
            .Select(member => new DocumentMemberAdapter(member.Role,
                plugin.Open(member.File, references), MemberIdentity)).ToArray();
        _router = new(_members);
    }

    public string Title => "XorJson character + global";
    public bool CanEdit => _members.All(member => member.Document.CanEdit);
    public IReadOnlyList<QueryableCollection> Collections => _members
        .SelectMany(member => member.Collections).ToArray();

    public ViewNode BuildView() => new SectionNode(Title,
    [
        new TextNode(GroupWarning) { Tone = Tone.Warning },
        .. _members.Select(member => (ViewNode)new SectionNode(member.Role, [member.Node(member.Document.BuildView())])),
    ]);

    public IReadOnlyList<ViewNode> ExpandNode(string lazyId) => Resolve(lazyId).ExpandNode(lazyId);

    public FieldValue GetValue(string valueId) => Resolve(valueId).GetValue(valueId);

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) =>
        Resolve(collectionId).Query(collectionId, query);

    public EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits)
    {
        var before = CaptureMemento();
        var changes = new List<ValueChange>();
        var warnings = new List<string> { GroupWarning };
        try
        {
            foreach (DocumentMemberEdits batch in _router.PartitionEdits(edits))
            {
                var outcome = batch.Member.ApplyEdits(batch.Edits);
                if (!outcome.Succeeded) throw new SaveFormatException(string.Join("; ", outcome.Errors.Select(error => error.Message)));
                changes.AddRange(outcome.Changes);
                warnings.AddRange(outcome.Warnings);
            }
            _ = Serialize();
            return new EditOutcome { Changes = changes, Warnings = warnings, StructureChanged = changes.Count > 0, Revert = before };
        }
        catch (Exception ex) when (ex is SaveFormatException or InvalidOperationException or KeyNotFoundException)
        {
            _ = RestoreMemento(before);
            return new EditOutcome { Errors = [new FieldError("group", ex is KeyNotFoundException ? UnknownMemberIdentity : ex.Message)] };
        }
    }

    public ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        var member = Resolve(actionId);
        if (member.Local(actionId) == "json/inspect")
            return member.InvokeAction(actionId, parameters);
        var before = CaptureMemento();
        try
        {
            var outcome = member.InvokeAction(actionId, parameters);
            _ = Serialize();
            return outcome with
            {
                Warnings = [GroupWarning, .. outcome.Warnings],
                Revert = before,
                StructureChanged = true,
            };
        }
        catch
        {
            _ = RestoreMemento(before);
            throw;
        }
    }

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
    {
        var member = Resolve(actionId);
        try
        {
            var plan = member.PlanAction(actionId, parameters, scope);
            return plan with { Errors = LocalErrors(member, plan.Errors) };
        }
        catch (KeyNotFoundException) { throw new KeyNotFoundException(UnknownMemberIdentity); }
    }

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
    {
        var member = Resolve(actionId);
        var before = CaptureMemento();
        try
        {
            // XorJson historically returns local diagnostic IDs, even though changes and skipped rows are qualified.
            var outcome = member.ApplyActionPlan(actionId, parameters, scope,
                plan with { Errors = plan.Errors.Select(error => error.ValueId.Length == 0
                    ? error : error with { ValueId = member.Prefix(error.ValueId) }).ToArray() });
            if (!outcome.Succeeded) return outcome with { Errors = LocalErrors(member, outcome.Errors) };
            _ = Serialize();
            return outcome with
            {
                Warnings = [GroupWarning, .. outcome.Warnings],
                Revert = before,
                StructureChanged = true,
            };
        }
        catch (KeyNotFoundException)
        {
            _ = RestoreMemento(before);
            throw new KeyNotFoundException(UnknownMemberIdentity);
        }
        catch
        {
            _ = RestoreMemento(before);
            throw;
        }
    }

    public IReadOnlyList<FileOutput> Serialize() => _members.SelectMany(member => member.Document.Serialize()).ToArray();
    public AssetContent? GetAsset(string assetId) => _references.GetAsset(assetId);

    public ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count) =>
        Resolve(blobId).ReadBlob(blobId, offset, count);

    public DocumentMemento CaptureMemento() => new XorJsonGroupSnapshot(_members.ToDictionary(
        member => member.Role, member => ((JsonSaveDocument)member.Document).CaptureDocumentMemento(), StringComparer.Ordinal));

    public DocumentMemento RestoreMemento(DocumentMemento memento)
    {
        if (memento is not XorJsonGroupSnapshot snapshot || !snapshot.HasRoles(_members.Select(member => member.Role)))
            throw new InvalidOperationException("The snapshot does not belong to this XorJson group.");
        var inverse = CaptureMemento();
        foreach (var member in _members) _ = member.Document.RestoreMemento(snapshot.For(member.Role));
        return inverse;
    }

    public void Dispose()
    {
        foreach (var member in _members) member.Document.Dispose();
    }

    private static FieldError[] LocalErrors(DocumentMemberAdapter member, IReadOnlyList<FieldError> errors) =>
        errors.Select(error => error.ValueId.Length == 0 ? error : error with { ValueId = member.Local(error.ValueId) }).ToArray();

    // Keep the existing public diagnostic while SDK-04 owns member selection and scope validation.
    private DocumentMemberAdapter Resolve(string id)
    {
        try { return _router.Resolve(id); }
        catch (KeyNotFoundException) { throw new KeyNotFoundException(UnknownMemberIdentity); }
    }
}
