using System.Buffers.Binary;
using System.Globalization;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Freezes each qualified slot's original star tier so edits cannot require an unproven level update.</summary>
internal sealed class MateriaEditPolicy
{
    public const uint MaximumTargetAp = 999_999;
    private readonly IntegerSpec?[] _ranges = new IntegerSpec?[GameplayFields.MateriaCount];

    public MateriaEditPolicy(byte[] bytes, Ff7Schema schema)
    {
        if (schema.IsDemo || bytes.Length == Ff7Schema.CommonFileLength)
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
            if (level == 0 || level > thresholds.Length || thresholds.Count(threshold => ap >= threshold) != level)
            {
                continue;
            }

            _ranges[slot] = IntegerSpec.UInt32 with
            {
                Min = thresholds[level - 1],
                Max = level < thresholds.Length ? thresholds[level] - 1 : Math.Max(MaximumTargetAp, ap),
            };
        }
    }

    public IntegerSpec? Range(int slot) => _ranges[slot];
}
