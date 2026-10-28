using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.BaldursGate3;

internal static class LsfTables
{
    internal static Dictionary<uint, string> ReadNames(ReadOnlySpan<byte> bytes)
    {
        var buckets = BinaryData.U32(bytes, 0);
        BinaryData.Require(buckets <= ushort.MaxValue + 1u, "Invalid LSOF name bucket count.");
        var names = new Dictionary<uint, string>();
        var position = sizeof(uint);
        for (uint bucket = 0; bucket < buckets; bucket++)
        {
            var count = BinaryData.U16(bytes, position);
            position += sizeof(ushort);
            for (uint chain = 0; chain < count; chain++)
            {
                var length = BinaryData.U16(bytes, position);
                position += sizeof(ushort);
                names.Add((bucket << LsfLayout.NameBucketShift) | chain, BinaryData.Utf8.GetString(BinaryData.Slice(bytes, position, length)));
                position += length;
                BinaryData.Require(names.Count <= BinaryData.MaxRecords, "Too many LSOF names.");
            }
        }

        BinaryData.Require(position == bytes.Length, "Unexpected bytes in the LSOF name table.");
        return names;
    }

    internal static LarianResource Read(Dictionary<uint, string> names, byte[] nodeData,
        byte[] attributeData, byte[] values, byte[] keys, bool adjacency)
    {
        var recordSize = adjacency ? LsfLayout.AdjacencyRecordSize : LsfLayout.CompactRecordSize;
        var nodeCount = RecordCount(nodeData, recordSize);
        var attributeCount = RecordCount(attributeData, recordSize);
        var nodes = new List<ResourceNode>(nodeCount);
        var attributes = new List<ResourceAttribute>(attributeCount);
        var roots = new List<int>();
        var firstAttributes = new int[nodeCount];
        for (var i = 0; i < nodeCount; i++)
        {
            var row = nodeData.AsSpan(i * recordSize, recordSize);
            var parent = BinaryData.I32(row, adjacency ? LsfLayout.ExtendedParent : LsfLayout.CompactParent);
            BinaryData.Require(parent >= -1 && parent < i, "Invalid LSOF parent reference (parents must precede children).");
            if (adjacency)
            {
                var sibling = BinaryData.I32(row, LsfLayout.ExtendedSibling);
                BinaryData.Require(sibling == -1 || (sibling > i && sibling < nodeCount), "Invalid LSOF sibling reference.");
            }

            nodes.Add(new ResourceNode(Name(BinaryData.U32(row, 0)), parent));
            firstAttributes[i] = BinaryData.I32(row, adjacency ? LsfLayout.ExtendedFirstAttribute : LsfLayout.CompactFirstAttribute);
            if (parent == -1)
            {
                roots.Add(i);
            }
            else
            {
                nodes[parent].Children.Add(i);
            }
        }

        var nextAttributes = new int[attributeCount];
        Array.Fill(nextAttributes, -1);
        var ownerLastAttribute = new Dictionary<int, int>();
        var valueOffset = 0;
        for (var i = 0; i < attributeCount; i++)
        {
            var row = attributeData.AsSpan(i * recordSize, recordSize);
            var typeAndLength = BinaryData.U32(row, LsfLayout.AttributeTypeAndLength);
            var length = (int)(typeAndLength >> LsfLayout.AttributeTypeBits);
            var typeId = typeAndLength & LsfLayout.AttributeTypeMask;
            BinaryData.Require(typeId <= (uint)LarianAttributeType.TranslatedFSString, $"Unsupported LSOF attribute type {typeId}.");
            if (adjacency)
            {
                nextAttributes[i] = BinaryData.I32(row, LsfLayout.AttributeLink);
                valueOffset = BinaryData.Size(BinaryData.U32(row, LsfLayout.ExtendedValueOffset));
            }
            else
            {
                // Compact owner tags are NOT necessarily node-table indices (LSLib).
                var owner = BinaryData.I32(row, LsfLayout.AttributeLink);
                BinaryData.Require(owner >= -1, "Invalid LSOF compact attribute owner tag.");
                if (ownerLastAttribute.TryGetValue(owner, out var previous))
                {
                    nextAttributes[previous] = i;
                }

                ownerLastAttribute[owner] = i;
            }

            BinaryData.Slice(values, valueOffset, length);
            var attribute = new ResourceAttribute(Name(BinaryData.U32(row, 0)), (LarianAttributeType)typeId,
                values.AsMemory(valueOffset, length));
            AttributeDecoder.Validate(attribute);
            attributes.Add(attribute);
            valueOffset += length;
        }

        var assigned = new bool[attributeCount];
        for (var i = 0; i < nodeCount; i++)
        {
            var attribute = firstAttributes[i];
            while (attribute != -1)
            {
                BinaryData.Require(attribute >= 0 && attribute < attributeCount && !assigned[attribute],
                    "Invalid, shared or cyclic LSOF attribute reference.");
                assigned[attribute] = true;
                nodes[i].Attributes.Add(attribute);
                attribute = nextAttributes[attribute];
            }
        }

        BinaryData.Require(assigned.All(value => value), "LSOF contains unreachable attributes.");
        var keyCount = RecordCount(keys, LsfLayout.KeyRecordSize);
        var keyedNodes = new HashSet<int>();
        for (var i = 0; i < keyCount; i++)
        {
            var nodeIndex = BinaryData.I32(keys, i * LsfLayout.KeyRecordSize);
            BinaryData.Require(nodeIndex >= 0 && nodeIndex < nodeCount && keyedNodes.Add(nodeIndex), "Invalid or duplicate LSOF key node reference.");
            nodes[nodeIndex].KeyAttribute = Name(BinaryData.U32(keys, i * LsfLayout.KeyRecordSize + LsfLayout.KeyName));
        }

        return new LarianResource("LSOF", nodes, attributes, roots);

        string Name(uint reference) => names.TryGetValue(reference, out var name) ? name :
            throw new SaveFormatException("Invalid LSOF name-table reference.");
    }

    private static int RecordCount(byte[] data, int recordSize)
    {
        BinaryData.Require(data.Length % recordSize == 0 && data.Length / recordSize <= BinaryData.MaxRecords,
            "Invalid LSOF table size or excessive record count.");
        return data.Length / recordSize;
    }
}
