using System.Buffers.Binary;
using System.Globalization;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed class ItemStatsInspection
{
    public const string CollectionId = "item-stats";
    private const ulong HashMultiplier = 0xC6A4A7935BD1E995;
    private const ulong CounterMultiplier = 0x35A98F4D286A90B9;
    private const int HashShift = 47;
    private const string Source = ExtendedItemReader.Source + " Modifier buffer format research: " +
        "https://github.com/WolvenKit/WolvenKit/blob/65ea74b014e11bb41659c65401e43d5ebe4d8d75/WolvenKit.RED4/Archive/IO/ModifiersBufferReader.cs";
    private static readonly string[] Buffers = ["modifiersBuffer", "forcedModifiersBuffer"];
    public ItemStatsInspection(CsavContainer container, CsavTree tree, IReadOnlyList<InventoryItem> items)
    {
        var rows = new List<InspectionRow>();
        try
        {
            if (!InventoryReader.IsVerifiedVersion(container)) throw new SaveFormatException("Stat inspection schema version is unverified.");
            var nodes = tree.Nodes.Where(n => n.Name == "StatsSystem").ToArray();
            if (nodes.Length != 1) throw new SaveFormatException("No unique stats package.");
            var package = new RedPackage(ProgressionData.PackageBytes(container.Payload, nodes[0]), false);
            if (package.Types.Count != 1 || package.Types[0] != "gameStatsStateMapStructure") throw new SaveFormatException("Unverified stats root.");
            var root = package.Chunk(0);
            var keys = root.StructArray(root.Require("keys", "array:gameStatsObjectID"));
            var values = root.StructArray(root.Require("values", "array:gameSavedStatsData"));
            if (keys.Count != values.Count) throw new SaveFormatException("Stats key/value count mismatch.");
            var itemKeys = keys.Select((key, index) => (key, index))
                .Where(pair => pair.key.Enum("idType", "gameStatIDType", "Invalid") == "ItemID")
                .ToLookup(pair => ScriptableReader.UInt64(pair.key, "entityHash", "Uint64"), pair => pair.index);
            foreach (var item in items)
            {
                var identity = ItemIdentity.FromInventory(item, container, tree);
                var identities = new List<(string Path, ItemIdentity Identity)> { ("inventory", identity) };
                try
                {
                    var extended = ExtendedItemReader.Read(container.Payload.AsSpan(tree.Nodes[item.NodeId].Offset, tree.Nodes[item.NodeId].Size));
                    if (extended.Root is { } part) identities.AddRange(ExtendedItemReader.Parts(part).Select(p => (p.Path, p.Part.Identity)));
                }
                catch (SaveFormatException ex) { rows.Add(Row(item.NodeId, "parts", "unparsed", "", "", ex.Message)); }
                foreach (var (path, instance) in identities)
                {
                    var hash = InstanceHash(instance);
                    var matches = itemKeys[hash].ToArray();
                    if (matches.Length != 1)
                    {
                        rows.Add(Row(item.NodeId, "join", "", $"0x{hash:X16}", "", matches.Length == 0 ? "No saved stats entry" : "Ambiguous stats key; not joined", path: path));
                        continue;
                    }
                    var data = values[matches[0]];
                    rows.Add(Row(item.NodeId, "join", "", $"0x{hash:X16}", "", "Unique saved ItemID stats-key match; final stats are not evaluated.", path: path));
                    foreach (var buffer in Buffers)
                    {
                        if (data.Find(buffer) is null) continue;
                        try { ReadBuffer(data.Raw(data.Require(buffer, "DataBuffer")), item.NodeId, path, buffer, rows); }
                        catch (SaveFormatException ex) { rows.Add(Row(item.NodeId, buffer, "unparsed", "", "", ex.Message, path: path)); }
                    }
                }
            }
        }
        catch (SaveFormatException ex) { rows.Clear(); Warning = ex.Message; }
        Table = new InspectionTable(CollectionId, "Saved item stat modifiers (read-only)", [new("node", "Inventory node", IntegerSpec.UInt32), new("part", "Inventory / part path"),
            new("buffer", "Buffer"), new("class", "Modifier class"), new("stat", "Stat hash"), new("value", "Stored modifier value"),
            new("details", "Operation / evidence")], rows,
            "Exact ItemID stats-key join from TweakDB ID + seed + unique counter. Flags remain part of equipment identity; the researched stats hash excludes them. " +
            "Stored modifiers are not computed final stats, item tier or cyberware capacity. Unknown stat hashes and unsupported modifier bodies remain raw and read-only.");
    }
    public string? Warning { get; }
    public InspectionTable Table { get; }
    public IReadOnlyList<ViewNode> Build() => [new TextNode(Warning ?? Table.Metadata.Description!)
        { Tone = Warning is null ? Tone.Info : Tone.Warning }, Table.View()];

    internal static ulong InstanceHash(ItemIdentity item)
    {
        unchecked
        {
            var mixed = (item.Counter == 0 ? item.Seed : item.Counter) * HashMultiplier;
            mixed ^= mixed >> HashShift;
            if (item.Counter != 0)
            {
                mixed = ((mixed * HashMultiplier) ^ item.Seed) * CounterMultiplier;
                mixed ^= mixed >> HashShift;
            }
            return ((mixed * HashMultiplier) ^ item.TweakId) * HashMultiplier;
        }
    }

    internal static void ReadBuffer(byte[] bytes, int node, string path, string buffer, List<InspectionRow> rows)
    {
        const int maximumModifiers = 4096;
        if (BinaryData.Int32(bytes, 0) != bytes.Length - 4) throw new SaveFormatException("Modifier buffer length mismatch.");
        var position = 4;
        var index = 0;
        while (position < bytes.Length)
        {
            if (index++ >= maximumModifiers) throw new SaveFormatException("Modifier count exceeds inspection limit.");
            var type = ExtendedItemReader.ReadString(bytes, ref position);
            var stat = BinaryData.UInt64(bytes, position);
            var operation = BinaryData.UInt32(bytes, position + 8);
            position += 12;
            string value;
            string details = $"Raw modifier operation {operation}; ";
            switch (type)
            {
                case "gameConstantStatModifier":
                    value = Float(bytes, position);
                    position += 4;
                    break;
                case "gameCombinedStatModifier":
                    details += $"reference stat 0x{BinaryData.UInt64(bytes, position):X16}; combination {BinaryData.UInt32(bytes, position + 8)}; relation {BinaryData.UInt32(bytes, position + 12)}; ";
                    value = Float(bytes, position + 16);
                    position += 20;
                    break;
                case "gameCurveStatModifier":
                    value = ExtendedItemReader.ReadString(bytes, ref position) + " / " + ExtendedItemReader.ReadString(bytes, ref position);
                    details += $"curve stat 0x{BinaryData.UInt64(bytes, position):X16}; opaque uint32 {BinaryData.UInt32(bytes, position + 8)}; ";
                    position += 12;
                    break;
                default: throw new SaveFormatException("Unverified modifier class; remaining buffer preserved.");
            }
            rows.Add(Row(node, buffer, type, $"0x{stat:X16}", value, details + "Final effect and named stat mapping unverified.", index, path));
        }
    }
    private static string Float(ReadOnlySpan<byte> bytes, int position)
    {
        var value = BinaryPrimitives.ReadSingleLittleEndian(BinaryData.Slice(bytes, position, 4));
        return float.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : throw new SaveFormatException("Nonfinite stat modifier.");
    }
    private static InspectionRow Row(int node, string buffer, string type, string stat, string value, string detail, int index = 0, string path = "inventory") =>
        new($"item-stats/{node}/{path}/{buffer}/{index}", new Dictionary<string, string> { ["node"] = node.ToString(CultureInfo.InvariantCulture), ["part"] = path,
            ["buffer"] = buffer, ["class"] = type, ["stat"] = stat, ["value"] = value, ["details"] = detail }, Source, Confidence.Unknown);
}
