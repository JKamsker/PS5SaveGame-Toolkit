using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GodOfWarRagnarok;

internal sealed class RagnarokDocument(string path, string name, byte[] bytes, RagnarokRecordIndex index, RagnarokReferences references) : BoundSaveDocument, IQueryableDocument, IActionPlanningDocument
{
    private const string BlobId = "save";
    internal const int RowsPerPage = 256;
    internal const int RawPageSize = 1024;
    internal const int RawGroupSize = 64 * 1024;
    private int PageSize => index.Preferences ? RowsPerPage : RawPageSize;
    private RagnarokCollections? _collections;
    private readonly RagnarokWrites _writes = new(bytes, index, references);
    private RagnarokCollections CollectionViews => _collections ??= new(this, index, references);
    public IReadOnlyList<QueryableCollection> Collections => CollectionViews.Metadata;
    public QueryableCollectionPage Query(string collectionId, QueryableCollectionQuery query) => CollectionViews.Query(this, collectionId, query);

    public override string Title => index.Preferences ? "Ragnarök · User preferences" : $"Ragnarök · {name}";
    public override bool CanEdit => !index.Preferences;

    public override ViewNode BuildView()
    {
        var overview = new List<ViewNode>
        {
            new TextNode(index.Preferences ? "Read-only settings research view; no settings writer is implemented." :
                "Experimental decoded counter edits. " + RagnarokWrites.IntegrityWarning + " " + RagnarokWrites.EvidenceWarning) { Tone = Tone.Warning },
            new StatsNode([
                new("File", name), new("Bytes", bytes.Length.ToString()),
                new("Indexed IDs", index.DirectoryCount.ToString()), new("Wallet rows", index.WalletCount.ToString()),
            ]),
            Number(0, "Timestamp-like word (inferred Unix seconds)", Confidence.Inferred),
            Number(RagnarokFormat.KindOffset, "Record kind word", Confidence.Unknown, hex: true),
            Number(RagnarokFormat.RevisionOffset, "Revision word", Confidence.Unknown, hex: true),
            SectionNode.Lazy("All outer header words", "header"),
        };
        if (index.Preferences)
        {
            overview.Add(new TextNode("696-byte settings record. Individual settings, ranges and integrity are unknown; byte labels are raw offsets."));
            return new TabsNode([new("Preferences", overview), new("Raw settings", RawRoot())]);
        }

        overview.Add(Field("metadata/location", "Location / quest description", new TextSpec(), () => FieldValue.Of(index.Description))
            with { Confidence = Confidence.Verified, Description = "NUL-terminated UTF-8 text present in the save metadata." });
        overview.Add(CounterField(RagnarokFormat.PlaySecondsOffset));
        overview.Add(Number(RagnarokFormat.SaveCounterOffset, "Save counter (inferred)", Confidence.Inferred));
        return new TabsNode([
            new("Overview", overview),
            new("Resources & XP", [RagnarokWrites.RestoreAction(), CollectionViews.View("resources"), CollectionViews.View("xp")]),
            new("Gear candidates", [CollectionViews.View("nested-ids"), CollectionViews.View("index-words")]),
            new("Skills", RagnarokSpatialViews.SkillRoots()),
            new("Realm map", [RagnarokSpatialViews.RealmMap(references), SectionNode.Lazy("Realm reference details", "realm-details")]),
            new("Quests & collectibles", [CollectionViews.View("collectibles"), CollectionViews.View("flags"),
                new TextNode("Named quest IDs, equipped gear and completion flags remain unknown. No completion or ownership is inferred from raw matches.") { Tone = Tone.Warning }]),
            new("Nested containers", [RagnarokNestedView.Build(index, references)]),
            new("Record index", [RagnarokTables.Regions(index), SectionNode.Lazy("Grammar evidence", "grammar-evidence"),
                new TextNode("Partial index only. Gaps, nesting, lengths outside these arrays and integrity dependencies remain unknown.") { Tone = Tone.Warning },
                CollectionViews.View("directory"),
                SectionNode.Lazy("Secondary ID list", "secondary", index.SecondaryCount.ToString())]),
            new("Resource anchors", [new TextNode("All occurrences of catalogued resource IDs, including duplicates outside the wallet. Adjacent words outside wallet rows are unknown."),
                SectionNode.Lazy("Resource occurrences", "anchors", index.Occurrences.Count.ToString())]),
            new("Integrity", [new TextNode(RagnarokWrites.IntegrityWarning + " This diagnostic tests finite unkeyed digest hypotheses; no authentication algorithm is known.") { Tone = Tone.Warning },
                SectionNode.Lazy("Digest coverage evidence", "integrity")]),
            new("Raw", RawRoot()),
        ]);
    }

    public override IReadOnlyList<ViewNode> ExpandNode(string lazyId)
    {
        foreach (var weapon in RagnarokSpatialViews.Weapons)
        {
            if (lazyId == $"skill-tree/{weapon.Id}") return RagnarokSpatialViews.SkillTree(weapon.Id, references.Skills);
            if (lazyId == $"skill-details/{weapon.Id}") return [RagnarokSpatialViews.SkillDetails(this, weapon.Id, references.Skills)];
        }
        if (lazyId == "realm-details") return [RagnarokSpatialViews.RealmDetails(this)];

        if (lazyId == "header")
        {
            return [RagnarokTables.Words(this, 0, RagnarokFormat.HeaderSize, false)];
        }

        if (lazyId == "grammar-evidence")
        {
            return [RagnarokTables.Evidence(index)];
        }

        if (lazyId == "integrity" && !index.Preferences)
        {
            return [RagnarokIntegrityView.Build(bytes, index)];
        }

        if (lazyId == "raw")
        {
            return Enumerable.Range(0, (bytes.Length + RawGroupSize - 1) / RawGroupSize)
                .Select(group => (ViewNode)SectionNode.Lazy($"Bytes 0x{group * RawGroupSize:X}", $"raw-group/{group}")).ToArray();
        }

        if (TryPage(lazyId, "raw-group", (bytes.Length + RawGroupSize - 1) / RawGroupSize, out var rawGroup))
        {
            var start = rawGroup * RawGroupSize;
            var end = Math.Min(start + RawGroupSize, bytes.Length);
            return Enumerable.Range(start / PageSize, (end - start + PageSize - 1) / PageSize)
                .Select(page => (ViewNode)SectionNode.Lazy($"Bytes 0x{page * PageSize:X}", $"raw-page/{page}")).ToArray();
        }

        if (TryPage(lazyId, "raw-page", (bytes.Length + PageSize - 1) / PageSize, out var rawPage))
        {
            var start = rawPage * PageSize;
            return [RagnarokTables.Words(this, start, Math.Min(PageSize, bytes.Length - start), index.Preferences)];
        }

        if (!index.Preferences)
        {
            foreach (var (kind, count) in new[] { ("directory", index.DirectoryCount), ("secondary", index.SecondaryCount),
                ("wallet", index.WalletCount), ("anchors", index.Occurrences.Count) })
            {
                if (lazyId == kind)
                {
                    return Enumerable.Range(0, (count + RowsPerPage - 1) / RowsPerPage)
                        .Select(page => (ViewNode)SectionNode.Lazy($"Rows {page * RowsPerPage}–{Math.Min(count, (page + 1) * RowsPerPage) - 1}", $"{kind}/{page}"))
                        .ToArray();
                }

                if (TryPage(lazyId, kind, (count + RowsPerPage - 1) / RowsPerPage, out var page))
                {
                    return [RagnarokTables.Records(this, index, references, kind, page * RowsPerPage, Math.Min(RowsPerPage, count - page * RowsPerPage))];
                }
            }
        }

        throw new KeyNotFoundException($"Unknown lazy node '{lazyId}'.");
    }

    public override IReadOnlyList<FileOutput> Serialize()
    {
        _ = RagnarokRecordIndex.Parse(bytes, index.Preferences);
        return [new(path, bytes.ToArray())];
    }

    public override EditOutcome ApplyEdits(IReadOnlyList<ValueEdit> edits) => base.ApplyEdits(edits) with
    { Warnings = [RagnarokWrites.IntegrityWarning, RagnarokWrites.EvidenceWarning] };

    public override ActionOutcome InvokeAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters) =>
        actionId.StartsWith(RagnarokInspection.Prefix, StringComparison.Ordinal)
            ? RagnarokInspection.Inspect(this, index, references, actionId, parameters)
        : actionId == RagnarokWrites.RestoreActionId
            ? new() { Message = "Preview and apply this action through F6 planning.", Warnings = [RagnarokWrites.IntegrityWarning, RagnarokWrites.EvidenceWarning] }
            : base.InvokeAction(actionId, parameters);

    public ActionPlan PlanAction(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope) =>
        _writes.Plan(actionId, parameters, scope);

    public EditOutcome ApplyActionPlan(string actionId, IReadOnlyDictionary<string, FieldValue> parameters, ActionScope scope, ActionPlan plan)
    {
        var current = PlanAction(actionId, parameters, scope);
        if (current.Errors.Count > 0) return new() { Errors = current.Errors, Warnings = current.Warnings };
        if (plan.Errors.Count > 0 || !current.Changes.SequenceEqual(plan.Changes) ||
            !current.SkippedRows.SequenceEqual(plan.SkippedRows) || !current.Warnings.SequenceEqual(plan.Warnings))
            return EditOutcome.Rejected(actionId, "The preview changed; preview the action again.") with { Warnings = current.Warnings };
        foreach (var change in current.Changes) _ = CounterField(_writes.Counter(change.ValueId).Offset);
        return ApplyEdits(current.Changes.Select(change => new ValueEdit(change.ValueId, change.NewValue)).ToArray());
    }

    internal FieldNode CounterField(int offset)
    {
        var counter = _writes.Counter(offset);
        var spec = IntegerSpec.UInt32 with { Max = counter.Imported };
        if (!counter.Resource) spec = spec with { Format = IntegerFormat.Duration, UnitsPerSecond = 1 };
        return Field(counter.ValueId, counter.Label, spec,
            () => FieldValue.Of((long)_writes.Read(counter)), value => _writes.Write(counter, value),
            value => _writes.Validate(counter, value)) with
        {
            Confidence = Confidence.Inferred,
            Description = $"Decoded LE u32 at 0x{offset:X}. Based on: {counter.Source} " +
                RagnarokWrites.EvidenceWarning + " " + RagnarokWrites.IntegrityWarning,
            Warning = RagnarokWrites.IntegrityWarning,
        };
    }

    public override ReadOnlyMemory<byte> ReadBlob(string blobId, long offset, int count)
    {
        if (blobId != BlobId)
        {
            throw new KeyNotFoundException($"Unknown blob '{blobId}'.");
        }

        if (offset < 0 || offset > bytes.Length || count < 0 || count > RawGroupSize || count > bytes.Length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Byte pages must fit the save and contain at most 64 KiB.");
        }

        // Return a copy so a caller cannot mutate the document's original bytes.
        return bytes.AsSpan((int)offset, count).ToArray();
    }

    internal uint ReadWord(int offset) => RagnarokFormat.UInt32(bytes, offset);
    internal ulong ReadId(int offset) => RagnarokFormat.UInt64(bytes, offset);

    internal FieldNode SkillReference(ReferenceRecord record) =>
        Field(RagnarokSpatialViews.SkillValueId(record.Key), record.Name, new ReferenceSpec(RagnarokSkillReferences.CatalogId),
            () => FieldValue.Of(record.Key)) with
        {
            Confidence = Confidence.Unknown,
            Description = $"{RagnarokSpatialViews.SkillWarning} {record.AttributionText}",
        };

    internal FieldNode RealmReference(string realm) =>
        Field($"realm-reference/{realm}", realm, new TextSpec(), () => FieldValue.Of(realm)) with
        { Confidence = Confidence.Unknown, Description = "Reference destination only; saved realm access and completion are unknown. God of War Wiki external guide." };

    internal FieldNode ResourceReference(int offset, string catalogId, string name) =>
        Field($"resource-reference/{offset:X}", name, new ReferenceSpec(catalogId),
            () => FieldValue.Of(RagnarokReferences.Key(ReadId(offset)))) with
        {
            Confidence = catalogId == RagnarokCommunityTables.OlderCatalog || references.Find(ReadId(offset))?.Category == "Unknown"
                ? Confidence.Unknown : Confidence.Inferred,
            Description = references.Find(ReadId(offset))?.Description + " " + references.Attribution(ReadId(offset)) +
                " Identity is read-only; replacement dependencies are unknown.",
        };

    internal FieldNode Number(int offset, string label, Confidence confidence, bool hex = false, int width = sizeof(uint))
    {
        var spec = width == sizeof(ulong) ? IntegerSpec.UInt64 : width == 1 ? IntegerSpec.UInt8 : IntegerSpec.UInt32;
        return Field($"raw/{offset:X}/{width}/{(hex ? "hex" : "decimal")}", label, spec with { Format = hex ? IntegerFormat.Hex : IntegerFormat.Decimal },
            () => FieldValue.Of(width == sizeof(ulong) ? ReadId(offset) : width == 1 ? bytes[offset] : ReadWord(offset)))
            with { Confidence = confidence, Description = $"Little-endian bytes at 0x{offset:X} ({width} bytes)." };
    }

    private IReadOnlyList<ViewNode> RawRoot() =>
    [
        new HexNode(BlobId, bytes.Length) { Title = "Current save bytes" },
        SectionNode.Lazy(index.Preferences ? "Raw setting bytes" : "Raw word interpretations", "raw"),
    ];

    private static bool TryPage(string id, string prefix, int pageCount, out int page)
    {
        page = -1;
        return id.StartsWith(prefix + "/", StringComparison.Ordinal) &&
            int.TryParse(id.AsSpan(prefix.Length + 1), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out page) && page >= 0 && page < pageCount;
    }
}
