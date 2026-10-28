namespace SaveToolkit.Plugins.BaldursGate3;

internal sealed record LsmfClass(int Row, int Index, string Class, string Subclass, ulong Level);

internal static class LsmfClasses
{
    internal static IReadOnlyList<LsmfClass> Read(LsmfAccess access)
    {
        var classes = access.Column("game.stats.v0.ClassesComponent", 16);
        if (classes is null) return [];
        BinaryData.Require(classes.Rows <= 10_000, "Too many LSMF class rows.");
        var result = new List<LsmfClass>();
        for (var row = 0; row < classes.Rows; row++)
        {
            var offset = classes.Offset + row * 16;
            var bytes = access.Model.Bytes;
            var range = access.Array(BinaryData.U64(bytes.Span, offset), BinaryData.U64(bytes.Span, offset + 8), 40, 32);
            for (var index = 0; index < range.Count; index++)
            {
                var record = range.Offset + index * 40;
                result.Add(new LsmfClass(row, index, LsmfAccess.Guid(bytes.Slice(record, 16)),
                    LsmfAccess.Guid(bytes.Slice(record + 16, 16)), BinaryData.U64(bytes.Span, record + 32)));
            }
        }
        return result;
    }
}
