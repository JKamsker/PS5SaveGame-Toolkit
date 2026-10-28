using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.BaldursGate3;

internal sealed record LsmfSpellBook(int Row, IReadOnlyList<string> Spells);

internal static class LsmfSpellBooks
{
    internal static IReadOnlyList<LsmfSpellBook> Read(LsmfAccess access)
    {
        var books = access.Column("game.spell.v3.SpellBookComponent", 16);
        var data = access.Column("game.spell.v3.SpellData", 72);
        var ids = access.Column("game.spell.v0.SpellId", 24);
        if (books is null || data is null || ids is null) return [];
        BinaryData.Require(books.Rows <= 10_000, "Too many LSMF spell books.");
        var result = new List<LsmfSpellBook>();
        var bytes = access.Model.Bytes.Span;
        var total = 0;
        for (var row = 0; row < books.Rows; row++)
        {
            var book = books.Offset + row * 16;
            var range = access.Array(BinaryData.U64(bytes, book), BinaryData.U64(bytes, book + 8), 72, 4096);
            BinaryData.Require(total <= 100_000 - range.Count, "LSMF spell occurrence budget exceeded.");
            total += range.Count;
            var spells = new List<string>();
            for (var index = 0; index < range.Count; index++)
            {
                var record = range.Offset + index * 72;
                _ = access.ColumnPointer((ulong)(record - 48), data, arenaRelative: true);
                // Measured PS5 profile: first word of each arena-relative SpellData record.
                var id = access.ColumnPointer(BinaryData.U64(bytes, record), ids, arenaRelative: true);
                var name = access.String(BinaryData.U64(bytes, id + 8), BinaryData.U32(bytes, id + 16));
                BinaryData.Require(name.Contains('_') && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.'),
                    "Unsupported LSMF spell identifier profile.");
                spells.Add(name);
            }
            result.Add(new LsmfSpellBook(row, spells));
        }
        return result;
    }
}
