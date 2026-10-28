using System.Text.Json;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Stages coherent XP/level and AP/star pairs before any binding is changed.</summary>
internal sealed class ProgressionEdits
{
    private readonly Dictionary<string, Pair> _pairs = new(StringComparer.Ordinal);

    public void Add(string pointsId, string levelId, uint[] thresholds, uint originalPoints, int originalLevel)
    {
        var pair = new Pair(pointsId, levelId, thresholds, originalPoints, originalLevel);
        _pairs.Add(pointsId, pair);
        _pairs.Add(levelId, pair);
    }

    public IReadOnlyList<ValueEdit> Expand(IReadOnlyList<ValueEdit> edits, Func<string, FieldValue> read)
    {
        var staged = new Dictionary<string, FieldValue>(StringComparer.Ordinal);
        foreach (var edit in edits)
        {
            if (!staged.TryAdd(edit.ValueId, edit.Value))
                throw new ArgumentException("Duplicate field edits are not allowed.");
        }

        foreach (var pair in edits.Where(edit => _pairs.ContainsKey(edit.ValueId)).Select(edit => _pairs[edit.ValueId]).Distinct())
        {
            var hasPoints = staged.TryGetValue(pair.PointsId, out var pointsValue);
            var hasLevel = staged.TryGetValue(pair.LevelId, out var levelValue);
            var points = hasPoints ? pointsValue!.AsUInt32() : read(pair.PointsId).AsUInt32();
            var level = hasLevel ? levelValue!.AsInt32() : pair.Level(points);
            if (level < 1 || level > pair.Thresholds.Length)
                throw new ArgumentException("Level is outside the source curve.");
            if (!hasPoints)
                points = pair.Thresholds[level - 1];
            if (pair.Level(points) != level &&
                !(hasPoints && hasLevel && points == pair.OriginalPoints && level == pair.OriginalLevel))
                throw new ArgumentException("Points and level conflict; edit either field or provide a coherent pair.");
            if (points != read(pair.PointsId).AsUInt32()) staged[pair.PointsId] = FieldValue.Of((long)points);
            if (level != read(pair.LevelId).AsInt32()) staged[pair.LevelId] = FieldValue.Of((long)level);
        }

        return [.. staged.Select(edit => new ValueEdit(edit.Key, edit.Value))];
    }

    public static uint[][] CharacterCurves(Ff7Schema schema)
    {
        using var stream = typeof(ProgressionEdits).Assembly.GetManifestResourceStream(
            "SaveToolkit.Plugins.FinalFantasy7.Resources.character-curves.json")!;
        using var json = JsonDocument.Parse(stream);
        return [.. json.RootElement.GetProperty(schema.IsRemake ? "remake" : "rebirth").EnumerateArray()
            .Select(row => row.EnumerateArray().Select(value => value.GetUInt32()).ToArray())];
    }

    private sealed record Pair(string PointsId, string LevelId, uint[] Thresholds, uint OriginalPoints, int OriginalLevel)
    {
        public int Level(uint points) => Math.Max(1, Thresholds.Count(threshold => points >= threshold));
    }
}
