using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed class ProgressionEditor(ProgressionData data, byte[] payload)
{
    private const int MaximumPointBalance = 100_000; // Editor policy, not an engine limit.
    private const int MinimumAttribute = 3;
    private const int MaximumAttribute = 20;
    private const int MaximumExperience = 100_000; // Conservative editor policy; original larger values survive.
    private const int MaximumLevel = 60;
    private const int MaximumStreetCred = 50;

    public bool CanEdit => data.Proficiencies.Count > 0 || data.Attributes.Any(a => a.MirrorOffset.HasValue) ||
        data.Points.Any(p => p.Type is "Attribute" or "Primary" or "Espionage");

    public IReadOnlyList<ViewNode> Build(Func<ScalarBinding, FieldNode> bind)
    {
        var result = new List<ViewNode>();
        result.Add(new StatsNode(data.Proficiencies.Select(p => new Stat(p.Type == "Level" ? "Level" : "Street cred", p.Level.ToString())).ToArray()));
        foreach (var proficiency in data.Proficiencies)
        {
            void Add(string suffix, string label, int original, int policyMaximum, Func<int> get, Action<int> set)
            {
                var minimum = Math.Min(suffix == "level" ? 1 : 0, original);
                var maximum = Math.Max(policyMaximum, original);
                var refusal = suffix == "level" ? proficiency.LevelWarning : null;
                result.Add(bind(new($"progression/{proficiency.Type}/{suffix}", label, new IntegerSpec(minimum, maximum),
                    () => FieldValue.Of(get()), refusal is null ? value => set(value.AsInt32()) : null, value => Validate(value, minimum, maximum))) with
                {
                    Confidence = Confidence.Inferred,
                    Description = "Based on the PS5 2.21 SProficiency records and WolvenKit's pinned SProficiency schema. " +
                        ScriptableReader.Source + " " + WriteSafety.ProgressionWarning,
                    Warning = refusal ?? WriteSafety.ProgressionWarning,
                });
            }
            Add("level", proficiency.Type == "Level" ? "Stored level" : "Stored street cred", proficiency.OriginalLevel,
                proficiency.Type == "Level" ? MaximumLevel : MaximumStreetCred, () => proficiency.Level, value =>
                {
                    proficiency.Level = value;
                    if (proficiency.MirrorOffset is { } mirror) BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(mirror), value);
                });
            Add("xp", "Stored current XP", proficiency.OriginalExperience, MaximumExperience,
                () => proficiency.Experience, value => proficiency.Experience = value);
            Add("threshold", "Stored XP threshold (0 when omitted)", proficiency.OriginalThreshold, MaximumExperience,
                () => proficiency.Threshold, value => proficiency.Threshold = value);
            if (proficiency.MirrorOffset is { } offset)
                result.Add(bind(new($"stats/proficiency/{proficiency.Type}", "Existing forced level stat", FloatSpec.Single,
                    () => FieldValue.Of((double)BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(offset))))));
        }

        foreach (var attribute in data.Attributes)
        {
            var minimum = Math.Min(MinimumAttribute, attribute.Value);
            var maximum = Math.Max(MaximumAttribute, attribute.Value);
            Action<FieldValue>? setter = attribute.MirrorOffset is { } mirror ? value =>
            {
                BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(attribute.Offset), value.AsInt32());
                BinaryPrimitives.WriteSingleLittleEndian(payload.AsSpan(mirror), value.AsInt32());
            } : null;
            result.Add(bind(new($"progression/{attribute.Id}", attribute.Label, new IntegerSpec(minimum, maximum),
                () => FieldValue.Of(BinaryData.Int32(payload, attribute.Offset)), setter,
                value => Validate(value, minimum, maximum))) with {
                Warning = setter is null ? data.AttributeWarning ?? "No matching unique player stat mirror; edit refused."
                    : "Updates both development data and the forced player stat. Existing perks and point balances are preserved. " + WriteSafety.Warning,
            });
        }

        foreach (var point in data.Points.Where(p => p.Type is "Attribute" or "Primary" or "Espionage"))
        {
            var maximum = Math.Max(MaximumPointBalance, point.Original);
            var label = point.Type switch { "Attribute" => "Unspent attribute points", "Primary" => "Unspent perk points", _ => "Unspent relic points" };
            result.Add(bind(new($"progression/points/{point.Type}", label, new IntegerSpec(0, maximum),
                () => FieldValue.Of(point.Value), value => point.Value = value.AsInt32(), value => Validate(value, 0, maximum))) with {
                Warning = "Experimental: changes only unspent points. Spent points and unlocks are preserved; the editor cap is a policy. " + WriteSafety.Warning,
            });
        }

        result.Add(new SectionNode("Dependent player stats", data.Attributes.Where(a => a.MirrorOffset.HasValue)
            .Select(a => (ViewNode)bind(new($"stats/{a.Id}", $"{a.Label} forced stat", FloatSpec.Single,
                () => Mirror(a)))).ToArray()) { Collapsed = true });
        return result;
    }

    public IReadOnlyDictionary<string, FieldValue> DerivedValues() => data.Attributes.Where(a => a.MirrorOffset.HasValue)
        .Select(a => new KeyValuePair<string, FieldValue>($"stats/{a.Id}", Mirror(a)))
        .Concat(data.Proficiencies.Where(p => p.MirrorOffset.HasValue).Select(p => new KeyValuePair<string, FieldValue>(
            $"stats/proficiency/{p.Type}", FieldValue.Of((double)BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(p.MirrorOffset!.Value))))))
        .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    public void Verify(CsavContainer container, CsavTree tree)
    {
        var reopened = ProgressionData.TryRead(container, tree, out var warning)
            ?? throw new SaveFormatException($"Edited progression failed validation: {warning}");
        if (!reopened.Proficiencies.Select(p => (p.Type, p.Level, p.Experience, p.Threshold, p.MirrorOffset.HasValue)).SequenceEqual(
                data.Proficiencies.Select(p => (p.Type, p.Level, p.Experience, p.Threshold, p.MirrorOffset.HasValue))) ||
            !reopened.Points.Select(p => (p.Type, p.Value)).SequenceEqual(data.Points.Select(p => (p.Type, p.Value))) ||
            !reopened.Attributes.Select(a => (a.Id, a.Value, HasMirror: a.MirrorOffset.HasValue)).SequenceEqual(
                data.Attributes.Select(a => (a.Id, Value: BinaryData.Int32(payload, a.Offset), HasMirror: a.MirrorOffset.HasValue))))
        {
            throw new SaveFormatException("Edited player development values or their dependent stats did not survive serialization.");
        }
    }

    private FieldValue Mirror(ProgressionScalar attribute) => FieldValue.Of((double)
        BinaryPrimitives.ReadSingleLittleEndian(payload.AsSpan(attribute.MirrorOffset!.Value, 4)));

    private static string? Validate(FieldValue value, int minimum, int maximum) =>
        value.Kind != FieldValueKind.Integer || value.AsInteger() < minimum || value.AsInteger() > maximum
            ? $"Value must be an integer between {minimum} and {maximum}." : null;
}
