using System.Globalization;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests.Fakes;

/// <summary>Large synthetic collection used to exercise bounded query and binding behavior.</summary>
public sealed class QueryablePlugin(string collectionId = "items", bool emptySlots = false) : IGamePlugin
{
    public const int RowCount = 100_000;
    private static readonly byte[] Magic = "QRY1"u8.ToArray();

    public PluginInfo Info { get; } = new("test.queryable", "Queryable test format", "1.0.0")
    {
        Games = [new GameTitle("PPSA99997", "Queryable Game")],
    };

    public ProbeResult Probe(ISaveFile file) =>
        file.ReadHeader(Magic.Length).AsSpan().SequenceEqual(Magic)
            ? ProbeResult.Match(ProbeScore.Title, "Queryable test save")
            : ProbeResult.None;

    public QueryableDocument? LastOpened { get; private set; }

    public ISaveDocument Open(ISaveFile file)
    {
        if (!file.ReadHeader(Magic.Length).AsSpan().SequenceEqual(Magic))
        {
            throw new SaveFormatException("Not a queryable test save.");
        }

        return LastOpened = new QueryableDocument(file, collectionId, emptySlots);
    }

    public static byte[] Create() => [.. Magic, 0, 0, 0, 0];

    public sealed class QueryableDocument(ISaveFile file, string collectionId = "items", bool emptySlots = false) : BoundSaveDocument, IQueryableDocument, IActionPlanningDocument
    {
        private readonly Dictionary<int, long> _values = Enumerable.Range(0, RowCount).ToDictionary(i => i, i => (long)i);
        public Func<QueryableCollectionPage, QueryableCollectionPage>? TransformPage { get; set; }
        public int QueryCount { get; private set; }
        public int ApplyCount { get; private set; }
        public ActionScope? LastPlannedScope { get; private set; }
        public EmptyRowPresentation? EmptyRows { get; set; } = emptySlots ? new("name") : null;
        public QueryableCollectionQuery? LastQuery { get; private set; }

        private static readonly IReadOnlyList<QueryableColumn> Columns =
        [
            new("id", "Id", IntegerSpec.UInt32),
            new("name", "Name", new TextSpec { MaxLength = 32 }),
            new("value", "Value", IntegerSpec.UInt32),
        ];

        public override string Title => "Queryable inventory";
        public override bool CanEdit => true;

        public IReadOnlyList<QueryableCollection> Collections =>
            [new(collectionId, "Items", Columns, RowCount) { Description = "Synthetic 100k-row collection.", EmptyRows = EmptyRows }];

        public override ViewNode BuildView() =>
            new SectionNode("Large collections", [new QueryableTableNode(collectionId, "Items", Columns, RowCount) { EmptyRows = EmptyRows },
                new ActionNode("increment", "Increment selected") { SupportsPlanning = true }]);

        public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query)
        {
            QueryCount++;
            LastQuery = query;
            var rows = Enumerable.Range(0, RowCount).Select(CreateRecord);
            if (!string.IsNullOrWhiteSpace(query.Text))
            {
                var text = query.Text.Trim();
                rows = rows.Where(r => r.id.ToString(CultureInfo.InvariantCulture).Contains(text, StringComparison.OrdinalIgnoreCase)
                    || $"Item {r.id:D6}".Contains(text, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var (key, filter) in query.Filters)
            {
                if (key.Equals("value", StringComparison.OrdinalIgnoreCase) && long.TryParse(filter, out var value)) rows = rows.Where(r => _values[r.id] == value);
                else if (key.Equals("name", StringComparison.OrdinalIgnoreCase)) rows = rows.Where(r => $"Item {r.id:D6}".Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            rows = query.SortColumn?.ToLowerInvariant() switch
            {
                "name" => query.Descending ? rows.OrderByDescending(r => r.id) : rows.OrderBy(r => r.id),
                "value" => query.Descending ? rows.OrderByDescending(r => _values[r.id]) : rows.OrderBy(r => _values[r.id]),
                "id" => query.Descending ? rows.OrderByDescending(r => r.id) : rows.OrderBy(r => r.id),
                _ => rows,
            };
            var matches = rows.ToList();
            var hideEmpty = EmptyRows is not null && !(query.IncludeEmpty ?? !EmptyRows.HideByDefault);
            var hiddenEmptyCount = hideEmpty ? matches.Count(row => IsEmpty(row.id)) : 0;
            var filtered = hideEmpty ? matches.Where(row => !IsEmpty(row.id)).ToList() : matches;
            var page = filtered.Skip(query.Cursor is null ? query.Offset : DecodeCursor(query.Cursor)).Take(query.Limit).Select(r => BuildRow(r.id)).ToList();
            var next = (query.Cursor is null ? query.Offset : DecodeCursor(query.Cursor)) + page.Count < filtered.Count
                ? ((query.Cursor is null ? query.Offset : DecodeCursor(query.Cursor)) + page.Count).ToString(CultureInfo.InvariantCulture)
                : null;
            var result = new QueryableCollectionPage(collectionId, Columns, page, RowCount, filtered.Count, query.Cursor is null ? query.Offset : DecodeCursor(query.Cursor), query.Limit)
            { NextCursor = next, HiddenEmptyCount = hiddenEmptyCount };
            return TransformPage?.Invoke(result) ?? result;
        }

        public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope)
        {
            LastPlannedScope = scope;
            return new ActionPlan { Changes = (scope.RowKeys ?? []).Select(key =>
            {
                var value = _values[int.Parse(key, CultureInfo.InvariantCulture)];
                return new PlannedChange($"items/{key}/value", $"Item {key} value", FieldValue.Of(value), FieldValue.Of(value + 1));
            }).ToArray() };
        }

        public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
        {
            ApplyCount++;
            return ApplyEdits(plan.Changes.Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray());
        }

        public override IReadOnlyList<FileOutput> Serialize() => [new FileOutput(file.RelativePath, Create())];

        private (int id, long value) CreateRecord(int id) => (id, _values[id]);

        private TableRow BuildRow(int id)
        {
            var idValue = Field($"items/{id}/id", "Id", IntegerSpec.UInt32, () => FieldValue.Of((long)id), _ => { });
            var name = Field($"items/{id}/name", "Name", new TextSpec { MaxLength = 32 }, () => FieldValue.Of($"Item {id:D6}"), _ => { });
            var value = Field($"items/{id}/value", "Value", IntegerSpec.UInt32, () => FieldValue.Of(_values[id]), v => _values[id] = v.AsInt64());
            return new TableRow(id.ToString(CultureInfo.InvariantCulture), new Dictionary<string, ViewNode> { ["id"] = idValue, ["name"] = name, ["value"] = value }) { IsEmpty = IsEmpty(id) };
        }

        private bool IsEmpty(int id) => EmptyRows is not null && id % 2 == 0;

        private static int DecodeCursor(string cursor) => int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var offset) && offset >= 0 ? offset : 0;
    }
}
