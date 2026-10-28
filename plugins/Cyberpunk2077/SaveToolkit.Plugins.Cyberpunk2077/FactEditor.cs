using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed class FactEditor
{
    private readonly Dictionary<string, int> _offsets = new(StringComparer.Ordinal);
    private readonly byte[] _payload;
    private readonly bool _canEdit;
    private readonly uint _maximum;

    public FactEditor(CsavContainer container, CsavTree tree, byte[] payload,
        IReadOnlyDictionary<int, IReadOnlyList<SaveFact>> tables)
    {
        _payload = payload;
        _canEdit = InventoryReader.IsVerifiedVersion(container);
        _maximum = Math.Max(1U, tables.Values.SelectMany(facts => facts).Select(fact => fact.Value).DefaultIfEmpty().Max());
        foreach (var (nodeId, facts) in tables)
        {
            var node = tree.Nodes[nodeId];
            var position = sizeof(int);
            _ = BinaryData.PackedInt(container.Payload.AsSpan(node.Offset, node.Size), ref position);
            var valuesOffset = node.Offset + position + facts.Count * sizeof(uint);
            for (var index = 0; index < facts.Count; index++) _offsets.Add($"facts/{nodeId}/{index}", valuesOffset + index * sizeof(uint));
        }
    }

    public bool CanEdit => _canEdit && _offsets.Count > 0;
    public uint Value(int table, int index) => BinaryData.UInt32(_payload, _offsets[$"facts/{table}/{index}"]);
    public FieldNode Field(string id, Func<ScalarBinding, FieldNode> bind)
    {
        var offset = _offsets[id];
        return bind(new(id, "Raw saved fact value", IntegerSpec.UInt32 with { Max = _maximum },
            () => FieldValue.Of((ulong)BinaryData.UInt32(_payload, offset)),
            _canEdit ? value => BinaryPrimitives.WriteUInt32LittleEndian(_payload.AsSpan(offset), value.AsUInt32()) : null,
            value => value.Kind != FieldValueKind.Integer || value.AsInteger() < 0 || value.AsInteger() > _maximum
                ? "Raw fact value must be within the nonnegative observed corpus range (including 0 and 1)." : null)) with
        {
            Confidence = Confidence.Unknown,
            Warning = "Raw uint32 edit; quest stages, callbacks and related facts are not updated. " + WriteSafety.Warning,
        };
    }
}
