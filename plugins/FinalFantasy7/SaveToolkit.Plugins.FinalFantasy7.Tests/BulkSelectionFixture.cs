using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

/// <summary>Authored gameplay framing and deliberately staged rows, never private sample bytes.</summary>
internal sealed class BulkSelectionFixture : IDisposable
{
    public const int UnloadedSlot = 200;
    public const int IneligibleSlot = 201;
    public const int ZeroOrSummonSlot = 202;
    public const int ForeignTypeSlot = 203;
    public const int UnknownSlot = 204;
    public const int EquipmentSlot = 205;
    public const int TargetQuantity = 99;
    public const int TargetStars = 2;
    public const string Remake = "PPSA01911";
    public const string Rebirth = "PPSA08664";
    public const string Demo = "PPSA18936";
    private readonly GameplayLayout _layout;
    private readonly DiskSaveFile _file;
    private readonly ReferenceContext _references;
    private readonly string _storageRoot;
    private readonly FinalFantasy7Plugin _plugin = new();

    public BulkSelectionFixture(string title, string actionId, DocumentSessionOptions? options = null,
        Action<byte[], Ff7Schema>? arrange = null)
    {
        ActionId = actionId;
        CollectionId = actionId == InventoryPlanning.ActionId ? "inventory" : "materia";
        var schema = Ff7Schema.ForTitle(title)!;
        _layout = GameplayLayout.For(schema);
        Type = CollectionId == "inventory"
            ? uint.Parse(EmbeddedCatalogs.Records(schema, "ff7r-items").First(record => record.Category == "Items" &&
                !record.Name.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase)).Key, CultureInfo.InvariantCulture)
            : schema.IsRemake ? 10004U : 10020U;
        var bytes = SyntheticGameplay.Create(title).Bytes;
        if (CollectionId == "inventory")
        {
            foreach (var slot in new[] { 0, 1, UnloadedSlot }) Inventory(slot, Type, 1);
            Inventory(IneligibleSlot, Type, TargetQuantity);
            Inventory(ZeroOrSummonSlot, Type, 0);
            Inventory(ForeignTypeSlot, _layout.GilType, 10);
            const uint unknownType = 0xDEADBEEF;
            Inventory(UnknownSlot, unknownType, 1);
            var weapon = EmbeddedCatalogs.Records(schema, "ff7r-items").First(record => record.Category == "Weapons");
            Inventory(EquipmentSlot, uint.Parse(weapon.Key, CultureInfo.InvariantCulture), 1);
        }
        else
        {
            foreach (var slot in new[] { 0, 1, UnloadedSlot }) Materia(slot, Type, 1);
            Materia(IneligibleSlot, Type, 0); // Incoherent stored stars: must remain locked.
            var catalog = EmbeddedCatalogs.Records(schema, "ff7r-materia");
            Materia(ZeroOrSummonSlot, uint.Parse(catalog.First(record => record.Category == "Summon Materia").Key, CultureInfo.InvariantCulture), 1);
            Materia(ForeignTypeSlot, uint.Parse(catalog.First(record => record.Category != "Summon Materia" && record.Key != TypeText).Key,
                CultureInfo.InvariantCulture), 1);
            TargetAp = uint.Parse(catalog.Single(record => record.Key == TypeText).Attributes["apThresholds"].Split(',')[1],
                CultureInfo.InvariantCulture);
            Assert.True(TargetAp > 1);
        }
        arrange?.Invoke(bytes, schema);
        Original = bytes;
        _storageRoot = Path.Combine(TestScratch.ProcessFolder, "ff7-bulk-" + Guid.NewGuid().ToString("N"));
        var set = SaveSetImporter.Import([new ImportSource($"{title}/sdimg_TEST/{schema.Prefix}000",
            () => new MemoryStream(bytes, writable: false))], _storageRoot);
        _file = Assert.Single(set.AllDiskFiles);
        _references = new ReferencePackStore([_plugin]).Pin(_plugin, title);
        Document = OpenDocument();
        Session = new("bulk-selection", _file, _plugin, Document, references: _references, options: options);
        _ = Session.BuildView();

        void Inventory(int slot, uint type, uint quantity)
        {
            var at = _layout.InventoryOffset + slot * _layout.InventoryStride;
            Write(at, type);
            Write(at + GameplayLayout.QuantityDelta, quantity);
        }
        void Materia(int slot, uint type, byte stars)
        {
            Write(_layout.MateriaIdOffset + slot * GameplayLayout.MateriaStride, type);
            bytes[_layout.MateriaLevelOffset + slot * GameplayLayout.MateriaStride] = stars;
        }
        void Write(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
    }

    public string ActionId { get; }
    public string CollectionId { get; }
    public uint Type { get; }
    public string TypeText => Type.ToString(CultureInfo.InvariantCulture);
    public uint TargetAp { get; }
    public byte[] Original { get; }
    public ISaveDocument Document { get; }
    public DocumentSession Session { get; }
    public IActionPlanningDocument Planner => (IActionPlanningDocument)Document;
    public string Key(int slot) => $"{CollectionId}/{slot}";
    public IReadOnlyList<int> MatchingSlots => CollectionId == "inventory"
        ? [0, 1, UnloadedSlot, IneligibleSlot, ZeroOrSummonSlot] : [0, 1, UnloadedSlot, IneligibleSlot];
    public string PrimaryId(int slot) => Key(slot) + (CollectionId == "inventory" ? "/quantity" : "/ap");
    public IReadOnlyList<string> OwnedIds(int slot) => CollectionId == "inventory"
        ? [PrimaryId(slot)] : [PrimaryId(slot), Key(slot) + "/level"];
    public byte[] Bytes() => Assert.Single(Document.Serialize()).Content.ToArray();
    public ISaveDocument OpenDocument() => _plugin.Open(_file, _references);

    public IReadOnlyDictionary<string, FieldValue> Parameters => CollectionId == "inventory"
        ? new Dictionary<string, FieldValue> { ["quantity"] = FieldValue.Of((long)TargetQuantity) }
        : new Dictionary<string, FieldValue>
        {
            ["type"] = FieldValue.Of(TypeText),
            [ActionId == MateriaPlanning.StarActionId ? "stars" : "ap"] =
                FieldValue.Of(ActionId == MateriaPlanning.StarActionId ? TargetStars : (long)TargetAp),
        };

    public Dictionary<string, JsonElement> WireParameters => Parameters.ToDictionary(pair => pair.Key, pair =>
        JsonSerializer.SerializeToElement(pair.Key == "type" ? pair.Value.AsString() : pair.Value.AsInteger().ToString(CultureInfo.InvariantCulture)));

    public QueryableCollectionQuery MatchingQuery(bool includeEmpty = false) => new()
    {
        Text = $"0x{Type:X8}", Filters = new Dictionary<string, string> { ["id"] = $"0x{Type:X8}" },
        SortColumn = "slot", Descending = true, IncludeEmpty = includeEmpty, Offset = 1, Limit = 1,
    };

    public ActionPreview Preview(ActionScope scope) => Session.PlanAction(ActionId, WireParameters, scope,
        expectedRevision: null, cancellationToken: TestContext.Current.CancellationToken);

    public IEnumerable<int> AllowedOffsets(IEnumerable<int> slots) => slots.SelectMany(slot => CollectionId == "inventory"
        ? Enumerable.Range(_layout.InventoryOffset + slot * _layout.InventoryStride + GameplayLayout.QuantityDelta, sizeof(uint))
        : Enumerable.Range(_layout.MateriaApOffset + slot * GameplayLayout.MateriaStride, sizeof(uint))
            .Append(_layout.MateriaLevelOffset + slot * GameplayLayout.MateriaStride));

    public void Stage(ISaveDocument document)
    {
        var queryable = (IQueryableDocument)document;
        foreach (var slot in new[] { 0, 1 })
            _ = queryable.Query(CollectionId, new() { Filters = new Dictionary<string, string> { ["slot"] = slot.ToString(CultureInfo.InvariantCulture) } });
        var outcome = document.ApplyEdits([new(PrimaryId(0), FieldValue.Of(CollectionId == "inventory" ? 2L : 1L))]);
        Assert.True(outcome.Succeeded, string.Join("; ", outcome.Errors.Select(error => error.Message)));
        Assert.NotEmpty(outcome.Changes);
    }

    public void AssertApplyAndHistory(ActionPreview preview, params int[] slots)
    {
        Assert.Empty(preview.Plan.Errors);
        Assert.NotNull(preview.PlanId);
        Assert.NotEmpty(preview.Plan.Changes);
        Assert.Equal(Original, Bytes());
        Assert.False(Session.State.CanUndo);
        Assert.True(Session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Ok);
        var edited = Bytes();
        ByteChangeAssertions.OnlyOffsetsChanged(Original, edited, AllowedOffsets(slots));
        using var reopened = _plugin.Open(MemorySaveSet.SingleFile(_file.FileName, edited, _file.Directory.TitleId), _references);
        foreach (var slot in slots)
            _ = ((IQueryableDocument)reopened).Query(CollectionId, new() { Filters = new Dictionary<string, string> { ["slot"] = slot.ToString(CultureInfo.InvariantCulture) } });
        Assert.All(preview.Plan.Changes, change => Assert.Equal(change.NewValue, reopened.GetValue(change.ValueId)));
        Assert.True(Session.Undo().Ok);
        Assert.False(Session.State.CanUndo);
        Assert.Equal(Original, Bytes());
        Assert.True(Session.Redo().Ok);
        Assert.Equal(edited, Bytes());
        Assert.True(Session.ApplyActionPlan(preview.PlanId, preview.Revision, cancellationToken: TestContext.Current.CancellationToken).Stale);
    }

    public void Dispose()
    {
        Session.Dispose();
        Directory.Delete(_storageRoot, recursive: true);
    }
}
