using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.UnrealGvas.Hogwarts;

/// <summary>Storage-only overview. Reads neither register bindings nor choose an authoritative image.</summary>
internal static class HogwartsTalentSummary
{
    private sealed record Points(string Display, bool Comparable = false);
    private sealed record Summary(HogwartsDatabase Database, Points Points, long? TalentRows, string RowDisplay);

    public static IReadOnlyList<ViewNode> Build(IReadOnlyList<HogwartsDatabase> databases)
    {
        var summaries = databases.Select(Read).ToArray();
        var nodes = new List<ViewNode>();
        foreach (var summary in summaries)
        {
            nodes.Add(new SectionNode(summary.Database.Name + " · talent storage", [
                new StatsNode([
                    new Stat("Stored talent points", summary.Points.Display),
                    new Stat("Stored talent-record rows", summary.RowDisplay),
                ]),
                new TextNode($"Source image property: {summary.Database.Name}. " +
                    "Points: MiscDataDynamic, exact Player0/PerkPoints tuple, DataValue. " +
                    "Records: PerkDynamic row count (PerkID/Level schema). " +
                    "Inferred meaning; image role and player association are unverified. " +
                    "Rows are not spent points, talent costs or applied effects. Stored points are not a proven spendable balance. Read-only summary. " +
                    HogwartsDiscoveryFields.Attribution) { Tone = Tone.Info },
            ]));
        }
        if (summaries.Length == 2 && Differ(summaries[0], summaries[1]))
            nodes.Add(new TextNode("Talent storage differs between database images. Each summary retains its own values; neither image is selected as live or authoritative, and values are never added together.") { Tone = Tone.Info });
        return nodes;
    }

    private static Summary Read(HogwartsDatabase database)
    {
        var perks = database.Tables.SingleOrDefault(table => table.Name == "PerkDynamic");
        bool knownPerks = HasColumns(perks, "PerkID", "Level");
        string rows = perks is null ? "Unavailable (PerkDynamic missing)" : !knownPerks
            ? "Unavailable (unknown PerkDynamic columns)" : perks.Count.ToString(CultureInfo.InvariantCulture);
        return new(database, ReadPoints(database), knownPerks ? perks!.Count : null, rows);
    }

    private static Points ReadPoints(HogwartsDatabase database)
    {
        var table = database.Tables.SingleOrDefault(table => table.Name == "MiscDataDynamic");
        if (table is null) return new("Unavailable (MiscDataDynamic missing)");
        if (!HasColumns(table, "DataOwner", "DataName", "DataValue"))
            return new("Unavailable (unknown MiscDataDynamic columns)");
        try
        {
            var rows = database.QueryTalentPoints().Rows;
            if (rows.Count == 0) return new("Unavailable (Player0/PerkPoints missing)");
            if (rows.Count > 1) return new("Ambiguous (multiple Player0/PerkPoints rows)");
            var row = rows[0];
            string type = (string)row[0]!;
            if (type == "null") return new("Unavailable (SQL NULL)");
            if (type == "integer") return new("SQLite integer: " + HogwartsTable.Text(row[1]), true);
            if (type != "text") return new($"Unavailable (unexpected SQLite {type})");
            if ((long)row[3]! != 0) return new("Unavailable (SQLite text contains NUL)");
            bool clipped = (long)row[2]! != 0;
            string suffix = clipped ? $" [clipped to {HogwartsDatabase.TalentPointTextLimit} characters]" : "";
            return new("SQLite text: " + JsonSerializer.Serialize((string)row[1]!) + suffix, !clipped);
        }
        catch (Exception error) when (error is InvalidDataException or SqliteException or SaveFormatException)
        {
            return new("Unavailable (bounded talent-point query failed)");
        }
    }

    private static bool HasColumns(HogwartsTable? table, params string[] columns) =>
        table is not null && columns.All(column => table.Columns.Contains(column, StringComparer.Ordinal));

    private static bool Differ(Summary first, Summary second) =>
        (first.Points.Comparable && second.Points.Comparable && first.Points.Display != second.Points.Display) ||
        (first.TalentRows.HasValue && second.TalentRows.HasValue && first.TalentRows != second.TalentRows);
}
