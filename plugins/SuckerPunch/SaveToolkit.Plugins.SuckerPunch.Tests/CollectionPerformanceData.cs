using System.Buffers.Binary;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

internal sealed class CollectionPerformanceData : IDisposable
{
    internal const int EquipmentCount = 512;
    internal CollectionPerformanceData(bool yotei, IReferenceContext? references = null)
    {
        var save = new SyntheticSave(yotei, prefixLength: 24 * 1024);
        var rows = new List<(string Name, int Offset, byte A, byte B)>();
        if (yotei)
        {
            foreach (var (name, offset) in save.Gear)
            {
                var index = rows.Count;
                save.Bytes[offset] = (byte)(index % 3);
                save.Bytes[offset + 1] = (byte)(index % 5);
                rows.Add((name, offset, save.Bytes[offset], save.Bytes[offset + 1]));
            }
        }
        else
        {
            var position = SyntheticSave.HeaderSize;
            for (var index = 0; index < EquipmentCount; index++)
            {
                var name = $"ArmorTest{EquipmentCount - index:D4}";
                var offset = position;
                BinaryPrimitives.WriteUInt32LittleEndian(save.Bytes.AsSpan(position), (uint)name.Length);
                position += sizeof(uint);
                position += Encoding.ASCII.GetBytes(name, save.Bytes.AsSpan(position));
                rows.Add((name, offset, 0, 0));
            }
        }
        save.Repair();
        Original = save.Bytes;
        Rows = rows;
        Document = new SuckerPunchPlugin().Open(save.File(), references ?? IReferenceContext.Empty);
        Queryable = (IQueryableDocument)Document;
        CollectionId = Queryable.Collections.Single().Id;
    }

    internal byte[] Original { get; }
    internal IReadOnlyList<(string Name, int Offset, byte A, byte B)> Rows { get; }
    internal ISaveDocument Document { get; }
    internal IQueryableDocument Queryable { get; }
    internal string CollectionId { get; }
    internal QueryableCollectionPage Query(QueryableCollectionQuery query) => Queryable.Query(CollectionId, query);
    public void Dispose() => Document.Dispose();
}
