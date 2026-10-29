using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.Fakes;

public sealed class GroupDocument : BoundSaveDocument, IGroupedSaveDocument, IActionPlanningDocument, IQueryableDocument,
    IAdditionalOutputOwner
{
    private readonly Dictionary<string, Container> _containers = new(StringComparer.Ordinal);
    private int _nextId = 1;
    private string? _fault;
    private readonly Func<bool> _rejectApply;
    private readonly Func<bool> _rejectRestore;
    private readonly string? _systemPath;
    private readonly byte[]? _systemBytes;
    public bool IsDisposed { get; private set; }

    public GroupDocument(DocumentGroupInput input, Func<bool> rejectApply, Func<bool> rejectRestore,
        string? systemPath = null, byte[]? systemBytes = null)
    {
        _rejectApply = rejectApply;
        _rejectRestore = rejectRestore;
        _systemPath = systemPath;
        _systemBytes = systemBytes;
        foreach (var member in input.Members)
        {
            var (balance, items) = GroupPlugin.Read(member.File, member.Role == "character" ? "C" : "P");
            _containers.Add(member.Role, new(member.File.RelativePath, balance,
                items.Select(value => new Item(_nextId++, value)).ToList()));
        }
    }

    public override string Title => "Grouped containers";
    public IReadOnlyList<string> AdditionalOutputPaths => _systemPath is null ? [] : ["sce_sys/param.sfo"];
    public override ViewNode BuildView() => new SectionNode(Title,
    [
        Balance("character"), Balance("profile"),
        new SectionNode("Details", []) { LazyId = "details" },
        new ActionNode("transfer", "Transfer duplicate") { SupportsPlanning = true },
        new ActionNode("fail", "Fail after first member"),
        new ActionNode("bad-profile", "Corrupt second output"),
        new ActionNode("unowned", "Write unowned path"),
        new ActionNode("duplicate", "Return duplicate outputs"),
        new ActionNode("omitted", "Omit a member output"),
        new ActionNode("grow", "Grow a snapshot"),
        new ActionNode("inspect", "Inspect"),
        .. Collections.Select(c => (ViewNode)new QueryableTableNode(c.Id, c.Title, c.Columns, c.TotalCount)),
    ]);

    private FieldNode Balance(string role) => Field($"{role}/balance", $"{role} balance", IntegerSpec.Int32,
        () => FieldValue.Of(_containers[role].Balance), value => _containers[role].Balance = value.AsInt32());

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId) => lazyId == "details"
        ? [Field("profile/lazy", "Profile lazy balance", IntegerSpec.Int32,
            () => FieldValue.Of(_containers["profile"].Balance), value => _containers["profile"].Balance = value.AsInt32())]
        : throw new KeyNotFoundException();

    public IReadOnlyList<QueryableCollection> Collections => _containers.Select(pair =>
        new QueryableCollection(pair.Key, pair.Key, [new("value", "Value", IntegerSpec.Int32)], pair.Value.Items.Count)).ToArray();

    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
    {
        var items = _containers[collectionId].Items;
        return new(collectionId, [],
            items.Skip(query.Offset).Take(query.Limit).Select(item => new TableRow(item.Id.ToString(),
                new Dictionary<string, ViewNode> { ["value"] = Field($"item/{item.Id}", "Item", IntegerSpec.Int32,
                    () => FieldValue.Of(item.Value), value => item.Value = value.AsInt32()) })).ToArray(), items.Count, items.Count, query.Offset, query.Limit);
    }

    public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters)
    {
        if (actionId == "inspect") return new();
        if (actionId == "fail")
        {
            _containers["character"].Balance++;
            throw new InvalidOperationException("Second member rejected.");
        }
        if (actionId == "grow") _containers["profile"].Items.Add(new Item(_nextId++, 7));
        _fault = actionId;
        return new() { StructureChanged = true };
    }

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) => new()
    {
        Changes = [new("character/items", "Character count", FieldValue.Of(_containers["character"].Items.Count),
            FieldValue.Of(_containers["character"].Items.Count - 1)),
            new("profile/items", "Profile count", FieldValue.Of(_containers["profile"].Items.Count),
            FieldValue.Of(_containers["profile"].Items.Count + 1))],
        Errors = _containers["character"].Items.Count == 0 ? [new("", "No item to transfer.")] : [],
    };

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
    {
        var item = _containers["character"].Items[0];
        _containers["character"].Items.RemoveAt(0);
        if (_rejectApply()) return EditOutcome.Rejected("", "Second member rejected after removal.");
        // Preserve the identity of this duplicate; serialized item values alone cannot recover it.
        _containers["profile"].Items.Add(item);
        return new() { StructureChanged = true };
    }

    public override IReadOnlyList<FileOutput> Serialize()
    {
        var outputs = _containers.Select(pair => new FileOutput(pair.Value.Path,
            _fault == "bad-profile" && pair.Key == "profile" ? new byte[] { 0 } :
                GroupPlugin.Create(pair.Key == "character" ? "C" : "P", pair.Value.Balance,
                    pair.Value.Items.Select(item => item.Value).ToArray()))).ToList();
        if (_systemPath is not null) outputs.Add(new(_systemPath, _systemBytes!));
        if (_fault == "unowned") outputs.Add(new("unowned.dat", new byte[] { 0 }));
        if (_fault == "duplicate") outputs.Add(outputs[0]);
        if (_fault == "omitted") outputs.RemoveAt(1);
        return outputs;
    }

    public DocumentMemento CaptureMemento() => CaptureSnapshot();
    protected override DocumentMemento CaptureSnapshot() => DocumentMemento.Snapshot(
        new Snapshot(_containers.ToDictionary(pair => pair.Key, pair => new SavedContainer(pair.Value.Balance,
            pair.Value.Items.Select(item => (item.Id, item.Value)).ToArray())), _nextId, _fault),
        64 + _containers.Values.Sum(c => c.Items.Count * 32L));

    protected override void LoadSnapshot(DocumentMemento snapshot)
    {
        if (_rejectRestore()) throw new InvalidOperationException("Injected restore failure.");
        var state = ((SnapshotMemento<Snapshot>)snapshot).State;
        foreach (var (role, saved) in state.Containers)
        {
            _containers[role].Balance = saved.Balance;
            _containers[role].Items = saved.Items.Select(item => new Item(item.Id, item.Value)).ToList();
        }
        _nextId = state.NextId;
        _fault = state.Fault;
    }

    void IDisposable.Dispose() => IsDisposed = true;
    private sealed class Item(int id, int value) { public int Id { get; } = id; public int Value { get; set; } = value; }
    private sealed class Container(string path, int balance, List<Item> items)
    { public string Path { get; } = path; public int Balance { get; set; } = balance; public List<Item> Items { get; set; } = items; }
    private sealed record SavedContainer(int Balance, (int Id, int Value)[] Items);
    private sealed record Snapshot(Dictionary<string, SavedContainer> Containers, int NextId, string? Fault);
}
