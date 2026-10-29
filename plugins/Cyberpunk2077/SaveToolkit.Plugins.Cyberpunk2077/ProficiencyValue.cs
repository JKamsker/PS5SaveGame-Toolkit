using System.Buffers.Binary;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed class ProficiencyValue(string type, PackageClass record, int? mirrorOffset, string? levelWarning)
{
    private const string Int32Name = "Int32";

    public string Type { get; } = type;
    public PackageClass Record { get; } = record;
    public int OriginalLevel { get; } = record.Int("currentLevel");
    public int OriginalExperience { get; } = record.Int("currentExp");
    public int OriginalThreshold { get; } = record.Int("expToLevel");
    public int Level { get; set; } = record.Int("currentLevel");
    public int Experience { get; set; } = record.Int("currentExp");
    public int Threshold { get; set; } = record.Int("expToLevel");
    public int? MirrorOffset { get; } = mirrorOffset;
    public string? LevelWarning { get; } = levelWarning;
    public bool Changed => Level != OriginalLevel || Experience != OriginalExperience || Threshold != OriginalThreshold;
    public IEnumerable<string> RequiredNames
    {
        get
        {
            var names = new[]
            {
                ("currentLevel", Level != OriginalLevel), ("currentExp", Experience != OriginalExperience),
                ("expToLevel", Threshold != OriginalThreshold),
            }.Where(pair => pair.Item2).Select(pair => pair.Item1).ToArray();
            return names.Length == 0 ? Array.Empty<string>() : [.. names, Int32Name];
        }
    }

    public byte[] Rebuild(PackageClass patchedRecord)
    {
        var replacements = new Dictionary<string, byte[]>();
        void Replace(string name, int value, int original)
        {
            if (value == original) return;
            // Keep padding after an existing Int32. Missing zero-default properties may be materialized.
            var bytes = patchedRecord.Find(name) is { } field ? patchedRecord.Raw(field) : new byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            replacements.Add(name, bytes);
        }
        Replace("currentLevel", Level, OriginalLevel);
        Replace("currentExp", Experience, OriginalExperience);
        Replace("expToLevel", Threshold, OriginalThreshold);
        return patchedRecord.Rebuild(replacements);
    }
}
