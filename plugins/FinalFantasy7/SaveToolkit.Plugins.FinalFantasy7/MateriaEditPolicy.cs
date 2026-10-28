using System.Buffers.Binary;
using System.Globalization;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Qualifies ordinary source curves and registers reversible AP/star dependencies.</summary>
internal sealed class MateriaEditPolicy
{
    public const uint MaximumTargetAp = 999_999;
    private readonly uint[]?[] _curves = new uint[]?[GameplayFields.MateriaCount];
    private readonly IntegerSpec?[] _ranges = new IntegerSpec?[GameplayFields.MateriaCount];

    public MateriaEditPolicy(byte[] bytes, Ff7Schema schema, ProgressionEdits? progression = null)
    {
        if (bytes.Length == Ff7Schema.CommonFileLength)
        {
            return;
        }

        var catalog = EmbeddedCatalogs.Records(schema, "ff7r-materia").ToDictionary(record => uint.Parse(record.Key, CultureInfo.InvariantCulture));
        var layout = GameplayLayout.For(schema);
        for (var slot = 0; slot < _ranges.Length; slot++)
        {
            var idOffset = layout.MateriaIdOffset + slot * GameplayLayout.MateriaStride;
            var id = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(idOffset));
            var ap = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(idOffset + GameplayLayout.MateriaApDelta));
            var level = bytes[layout.MateriaLevelOffset + slot * GameplayLayout.MateriaStride];
            if (!catalog.TryGetValue(id, out var record) || record.Category == "Summon Materia" ||
                !record.Attributes.TryGetValue("apThresholds", out var curve))
            {
                continue;
            }

            var thresholds = curve.Split(',').Select(value => uint.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            if (thresholds.Length == 0 || thresholds[0] != 0 ||
                thresholds.Zip(thresholds.Skip(1)).Any(pair => pair.First >= pair.Second) ||
                level == 0 || level > thresholds.Length || thresholds.Count(threshold => ap >= threshold) != level)
            {
                continue;
            }

            _curves[slot] = thresholds;
            progression?.Add($"materia/{slot}/ap", $"materia/{slot}/level", thresholds, ap, level);
            _ranges[slot] = IntegerSpec.UInt32 with
            {
                Min = 0,
                Max = Math.Max(MaximumTargetAp, ap),
            };
        }
    }

    public int MaximumLevel(int slot) => _curves[slot]?.Length ?? byte.MaxValue;
    public int Level(int slot, uint ap) => _curves[slot]?.Count(threshold => ap >= threshold) ?? 0;
    public IntegerSpec? Range(int slot) => _ranges[slot];
}
