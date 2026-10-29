using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>Queryable live storage with explicit row provenance and save-local identity joins.</summary>
internal sealed class LsmfLiveViews(LsmfLiveSource source, Func<LsmfLiveSource, LsmfNumber, FieldNode> bind,
    Func<string, string, FieldValue, FieldNode> inspect, Func<LsmfLiveSource, LsmfSlot, FieldNode> bindSlot,
    Bg3References references)
{
    internal string NumbersId => source.Prefix + "/values";
    internal string SlotsId => source.Prefix + "/slots";
    internal string BooksId => source.Prefix + "/books";
    internal string ClassesId => source.Prefix + "/classes";
    internal string PartyId => source.Prefix + "/party";
    internal string InventoryId => source.Prefix + "/inventory";
    internal string GoldId => source.Prefix + "/gold";

    internal IReadOnlyList<QueryableCollection> Collections
    {
        get
        {
            var collections = new List<QueryableCollection>
            {
        new(NumbersId, "NewAge values (all entities)", [new("family", "Family"), new("row", "Source row"),
            new("value", "Stored value"), new("details", "Provenance")], source.Numbers.Count),
        new(SlotsId, "Current container entries (all owners)", [new("container", "Container row"), new("entity", "Item entity row"),
            new("slot", "Map key candidate"), new("action", "Reorder")], source.Slots.Count),
        new(BooksId, "Saved spell books (unjoined)", [new("row", "Book row"), new("count", "Spells"),
            new("details", "Spell graph and text list")], source.Books.Count),
        new(ClassesId, "Class selections (unjoined)", [new("icon", "Icon"), new("row", "Source row"), new("class", "Class GUID"),
            new("subclass", "Subclass GUID"), new("level", "Stored class level")], source.Classes.Count),
            };
            if (source.Identity is { } identity)
                collections.Add(new(PartyId, "Party identity joins", [new("party", "Party row"), new("body", "Body entity"),
                    new("state", "State entity"), new("template", "Template GUID"), new("inventory", "Primary inventory")], identity.Party.Count));
            if (source.Inventory is { } inventory)
            {
                collections.Add(new(InventoryId, "Current recursive inventory", [new("container", "Container row"),
                    new("item", "Item entity"), new("slot", "Slot"), new("owner", "Owner body"), new("path", "Path")], inventory.Entries.Count));
                collections.Add(new(GoldId, "Imported gold storage candidates (all owners)", [new("item", "Entity occurrences"),
                    new("quantity", "Imported contribution sum"), new("container", "Container row"),
                    new("owner", "Owner body"), new("confidence", "Confidence")], inventory.Gold.Count));
            }
            return collections;
        }
    }

    internal IReadOnlyList<ViewNode> Build() =>
    [
        new TextNode(LsmfLiveSource.Warning + " Effective abilities are caches, not base-score/respec controls. " +
            "HP/stat rows include NPCs and duplicate epochs; source row is not a party identity. Quantity totals cover all owners.") { Tone = Tone.Warning },
        .. source.Diagnostics.Select(message => (ViewNode)new TextNode(message) { Tone = Tone.Warning }),
        .. (source.Identity?.Diagnostics ?? []).Concat(source.Inventory?.Diagnostics ?? [])
            .Select(message => (ViewNode)new TextNode(message) { Tone = Tone.Warning }),
        .. Collections.Select(c => (ViewNode)new QueryableTableNode(c.Id, c.Title, c.Columns, c.TotalCount)),
        new ActionNode(LsmfLiveActions.Restore, "Restore imported stack quantities")
        { SupportsPlanning = true, Description = "Only explicitly decoded positive stack members, within imported ceilings. " + LsmfLiveSource.Warning },
    ];

    internal QueryableCollectionPage Query(string id, QueryableCollectionQuery query)
    {
        if (query.Offset < 0 || query.Limit is < 1 or > QueryableCollectionLimits.MaxPageSize || query.Cursor is not null)
            throw new ArgumentException("Use a bounded NewAge offset page without a cursor.");
        var collection = Collections.Single(c => c.Id == id);
        var columns = collection.Columns.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        if (query.Filters.Keys.Any(key => !columns.Contains(key)) || query.SortColumn is { } sort && !columns.Contains(sort))
            throw new ArgumentException("Unknown NewAge filter or sort column.");
        var items = Rows(id);
        var text = query.Text?.Trim();
        // Preserve native collection order while only constructing the requested rows.
        // Filtering/sorting still operates over the complete current model below.
        if (string.IsNullOrEmpty(text) && query.Filters.Count == 0 && query.SortColumn is null)
            return new QueryableCollectionPage(id, collection.Columns,
                items.Skip(query.Offset).Take(query.Limit).Select(item => item.Build()).ToArray(),
                collection.TotalCount, collection.TotalCount, query.Offset, query.Limit);
        var filtered = items.Where(item => (string.IsNullOrEmpty(text) || item.Text().Contains(text, StringComparison.OrdinalIgnoreCase)) &&
            query.Filters.All(filter => string.Equals(item.Value(filter.Key), filter.Value, StringComparison.OrdinalIgnoreCase))).ToArray();
        IEnumerable<QueryRow> ordered = filtered;
        if (query.SortColumn is { } column)
        {
            string Key(QueryRow row) => UInt128.TryParse(row.Value(column), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var number)
                ? number.ToString("D40", System.Globalization.CultureInfo.InvariantCulture) : row.Value(column);
            ordered = (query.Descending ? filtered.OrderByDescending(Key, StringComparer.OrdinalIgnoreCase) : filtered.OrderBy(Key, StringComparer.OrdinalIgnoreCase))
                .ThenBy(row => row.Key, StringComparer.Ordinal);
        }
        return new QueryableCollectionPage(id, collection.Columns, ordered.Skip(query.Offset).Take(query.Limit).Select(item => item.Build()).ToArray(),
            collection.TotalCount, filtered.Length, query.Offset, query.Limit);
    }

    private IEnumerable<QueryRow> Rows(string id) =>
         id == NumbersId
            ? source.Numbers.Select(n => new QueryRow(source.Prefix + "/" + n.Key, () => $"{n.Family} {n.Row} {n.Label} {source.Provenance(n)}",
                column => column switch { "family" => n.Family, "row" => Number(n.Row), "value" => Number(source.Get(n)), _ => "" }, () => NumberRow(n)))
            : id == SlotsId
                ? source.Slots.Select(s => new QueryRow(source.Prefix + "/" + s.Key, () => $"{s.ContainerRow} {source.EntityLabel(s.EntityRow)}",
                    column => column switch { "container" => Number(s.ContainerRow), "entity" => Number(s.EntityRow), "slot" => Number(source.Slot(s)), _ => "" }, () => SlotRow(s)))
                : id == BooksId ? source.Books.Select(b => new QueryRow(source.Prefix + $"/book/{b.Row}", () => $"{b.Row} {string.Join(' ', b.Spells)}",
                    column => column switch { "row" => Number(b.Row), "count" => Number(b.Spells.Count), _ => "" }, () => BookRow(b)))
                : id == ClassesId ? source.Classes.Select(c => new QueryRow(source.Prefix + $"/class/{c.Row}/{c.Index}", () => $"{c.Row} {c.Class} {c.Subclass} {references.Catalog(Bg3References.Classes)?.Find(c.Class)?.Name}",
                    column => column switch { "icon" => references.Catalog(Bg3References.Classes)?.Find(c.Class)?.Name ?? c.Class,
                        "row" => Number(c.Row), "class" => c.Class, "subclass" => c.Subclass,
                        "level" => c.Level.ToString(System.Globalization.CultureInfo.InvariantCulture), _ => "" }, () => ClassRow(c)))
                : id == PartyId && source.Identity is { } identity ? identity.Party.Select(PartyRow)
                : id == InventoryId && source.Inventory is { } inventory ? inventory.Entries.Select(InventoryRow)
                : id == GoldId && source.Inventory is { } gold ? gold.Gold.Select(GoldRow)
                : throw new ArgumentException("Unknown NewAge collection.");

    private static string Number(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private sealed record QueryRow(string Key, Func<string> Text, Func<string, string> Value, Func<TableRow> Build);

    internal IReadOnlyList<ViewNode> Expand(string id)
    {
        if (id == source.Prefix) return Build();
        var book = source.Books.FirstOrDefault(b => id == source.Prefix + $"/book/{b.Row}");
        if (book is null) throw new KeyNotFoundException("Unknown live NewAge section.");
        if (book.Spells.Count > 512)
            return [new TextNode("This spell book exceeds the graph budget; use its bounded raw resource view.") { Tone = Tone.Warning }];
        var nodes = book.Spells.Select((spell, occurrence) => new GraphItem($"spell/{book.Row}/{occurrence}", spell, GraphState.Unknown)
        {
            Branch = "Saved spell occurrences",
            Description = "Raw saved spell ID. Character, source, preparation and prerequisite relationships are not established.",
            Target = new ViewTarget(ViewTargetKind.Field, source.Prefix + $"/book/{book.Row}/spell/{occurrence}")
            { Path = ["tab:Live NewAge", "section:" + source.Prefix, "row:" + id, "section:" + id] },
        }).ToArray();
        return
        [
            new GraphNode($"Saved spell book {book.Row}", nodes, [],
                "Saved spell occurrences without prerequisite edges. All states are unknown: presence does not prove learned, prepared or usable."),
            .. book.Spells.SelectMany((spell, occurrence) => SpellDetails(book.Row, occurrence, spell)),
        ];
    }

    private IReadOnlyList<ViewNode> SpellDetails(int row, int occurrence, string spell)
    {
        var record = references.Catalog(Bg3References.Spells)?.Find(spell);
        return
        [
            references.Icon(record),
            Bg3References.Describe(inspect(source.Prefix + $"/book/{row}/spell/{occurrence}", "Stored spell ID", FieldValue.Of(spell)), record, spell) with
            {
                Spec = new ReferenceSpec(Bg3References.Spells) { ReadOnly = true, AllowCustom = true },
                Confidence = Confidence.Unknown,
                Warning = "No source/preparation/prerequisite join is inferred.",
            },
        ];
    }

    private TableRow NumberRow(LsmfNumber number) => new(source.Prefix + "/" + number.Key, new Dictionary<string, ViewNode>
    {
        ["family"] = new TextNode(number.Family),
        ["row"] = new TextNode(number.Row.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ["value"] = bind(source, number),
        ["details"] = new SectionNode("Storage evidence", [new TextNode(source.Provenance(number)), new TextNode($"Original offsets: {string.Join(", ", number.Offsets.Select(o => $"0x{o:X}"))}. " +
            "No current player/owner join is claimed. Unknown surrounding bytes remain unchanged.")]) { Collapsed = true },
    });

    private TableRow SlotRow(LsmfSlot slot) => new(source.Prefix + "/" + slot.Key, new Dictionary<string, ViewNode>
    {
        ["container"] = new TextNode(slot.ContainerRow.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ["entity"] = new TextNode(source.EntityLabel(slot.EntityRow)),
        ["slot"] = bindSlot(source, slot),
        ["action"] = new ActionNode(LsmfLiveActions.Swap, "Swap two selected map keys")
        { SupportsPlanning = true, Description = "Select exactly two entries in one container. No transfer or equipment change. " + LsmfLiveSource.Warning },
    });

    private TableRow BookRow(LsmfSpellBook book) => new(source.Prefix + $"/book/{book.Row}", new Dictionary<string, ViewNode>
    {
        ["row"] = new TextNode(book.Row.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ["count"] = new TextNode(book.Spells.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        ["details"] = SectionNode.Lazy("Saved spells (unknown source/preparation)", source.Prefix + $"/book/{book.Row}"),
    });

    private TableRow ClassRow(LsmfClass item)
    {
        var key = source.Prefix + $"/class/{item.Row}/{item.Index}";
        var record = references.Catalog(Bg3References.Classes)?.Find(item.Class);
        return new TableRow(key, new Dictionary<string, ViewNode>
        {
            ["icon"] = references.Icon(record),
            ["row"] = new TextNode(Number(item.Row)),
            ["class"] = Bg3References.Describe(inspect(key + "/class", "Class GUID", FieldValue.Of(item.Class)), record, item.Class) with
            {
                Spec = new ReferenceSpec(Bg3References.Classes) { ReadOnly = true, AllowCustom = true },
                Confidence = Confidence.Unknown,
            },
            ["subclass"] = inspect(key + "/subclass", "Subclass GUID", FieldValue.Of(item.Subclass)),
            ["level"] = inspect(key + "/level", "Stored class level (unjoined)", FieldValue.Of(item.Level)),
        });
    }

    private QueryRow PartyRow(LsmfPartyJoin join) => new(source.Prefix + $"/party/{join.PartyRow}",
        () => $"{join.PartyRow} {join.TemplateId} {join.BodyEntityRow} {join.StateEntityRow}",
        column => column switch
        {
            "party" => Number(join.PartyRow), "body" => Number(join.BodyEntityRow),
            "state" => join.StateEntityRow is { } state ? Number(state) : "unknown",
            "template" => join.TemplateId ?? "unknown",
            "inventory" => join.PrimaryInventoryEntityRow is { } inventory ? Number(inventory) : "unknown",
            _ => "",
        }, () => new TableRow(source.Prefix + $"/party/{join.PartyRow}", new Dictionary<string, ViewNode>
        {
            ["party"] = new TextNode(Number(join.PartyRow)),
            ["body"] = new TextNode(Number(join.BodyEntityRow)),
            ["state"] = new TextNode(join.StateEntityRow is { } state ? Number(state) : "Unknown") { Tone = Tone.Warning },
            ["template"] = inspect(source.Prefix + $"/party/{join.PartyRow}/template", "Template GUID", FieldValue.Of(join.TemplateId ?? "Unknown")) with
            {
                Confidence = join.IsAmbiguous ? Confidence.Unknown : Confidence.Inferred,
                Description = "Exact LSF Creator → entity → TemplateComponent bridge. It is a save-local identity join; no current-party or console validation is claimed.",
                Warning = LsmfLiveSource.Warning,
            },
            ["inventory"] = new TextNode(join.PrimaryInventoryEntityRow is { } inventory ? Number(inventory) : "Unknown") { Tone = Tone.Warning },
        }));

    private QueryRow InventoryRow(LsmfInventoryEntry entry) => new(source.Prefix + "/" + entry.Path,
        () => $"{entry.ContainerRow} {entry.ItemEntityRow} {source.Slot(entry)} {entry.Path}",
        column => column switch
        {
            "container" => Number(entry.ContainerRow), "item" => Number(entry.ItemEntityRow), "slot" => Number(source.Slot(entry)),
            "owner" => entry.OwnerBodyEntityRow is { } owner ? Number(owner) : "unknown", "path" => entry.Path, _ => "",
        }, () => new TableRow(source.Prefix + "/" + entry.Path, new Dictionary<string, ViewNode>
        {
            ["container"] = new TextNode(Number(entry.ContainerRow)), ["item"] = new TextNode(Number(entry.ItemEntityRow)),
            ["slot"] = new TextNode(Number(source.Slot(entry))), ["owner"] = new TextNode(entry.OwnerBodyEntityRow is { } owner ? Number(owner) : "Unknown") { Tone = Tone.Warning },
            ["path"] = new TextNode(entry.Path),
        }));

    private QueryRow GoldRow(LsmfGoldCandidate gold) => new(source.Prefix + $"/gold/{gold.Guid}",
        () => $"{gold.Guid} {string.Join(' ', gold.EntityRows)}", column => column switch
        {
            "item" => string.Join(", ", gold.EntityRows),
            "quantity" => gold.Stack is { } stack ? Number(stack.Original) : "unknown",
            "container" => gold.ContainerRow is { } container ? Number(container) : "unknown",
            "owner" => gold.OwnerBodyEntityRow is { } owner ? Number(owner) : "unknown",
            "confidence" => gold.IsAmbiguous ? "Unknown" : "Inferred",
            _ => "",
        }, () => new TableRow(source.Prefix + $"/gold/{gold.Guid}", new Dictionary<string, ViewNode>
        {
            ["item"] = new TextNode(string.Join(", ", gold.EntityRows)),
            ["quantity"] = gold.Stack is { } stack ? inspect(source.Prefix + $"/gold/{gold.Guid}/imported-quantity",
                "Imported gold contribution sum (not a party balance)", FieldValue.Of(stack.Original)) with
                {
                    Confidence = gold.IsAmbiguous ? Confidence.Unknown : Confidence.Inferred,
                    Description = "Original imported snapshot. Creator Entity → consistently gold Item template → repeated EntityId GUID occurrences → " +
                        "unique StackEntry contributions and current container candidate. No individual LSF Item occurrence or authoritative party owner is inferred. " +
                        "Source: independently measured PS5 bytes; DanFinchUK FORMAT.md, no licence published; reused with attribution per owner decision 2026-10-28, " +
                        "accessed 2026-10-29: https://github.com/DanFinchUK/bg3-savefile-parser/blob/9578ff7c46a1aa40c805f6b7beecf907f10fb3b8/FORMAT.md",
                    Warning = "Read-only research value; raw storage edits do not refresh this imported snapshot. " + LsmfLiveSource.Warning,
                } : new TextNode("Unknown: no unique explicit stack contribution; one is not assumed."),
            ["container"] = new TextNode(gold.ContainerRow is { } container ? Number(container) : "Unknown") { Tone = Tone.Warning },
            ["owner"] = new TextNode(gold.OwnerBodyEntityRow is { } owner ? Number(owner) : "Unknown") { Tone = Tone.Warning },
            ["confidence"] = new TextNode(gold.IsAmbiguous ? "Unknown: missing or duplicate storage join" : "Inferred storage; party balance unknown") { Tone = Tone.Warning },
        }));
}
