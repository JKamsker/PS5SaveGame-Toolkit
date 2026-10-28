using System.Buffers.Binary;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

internal sealed record ProgressionBinding(string Id, string Label, FieldSpec Spec, Func<FieldValue> Get,
    Action<FieldValue>? Set = null, Func<FieldValue, string?>? Validate = null);

internal sealed class ProgressionEditor(ProgressionData data, byte[] payload)
{
    private const int MaximumPointBalance = 100_000; // Editor policy, not an engine limit.
    private const int MinimumAttribute = 3;
    private const int MaximumAttribute = 20;
    private const string TransitionWarning = "Read-only: the PS5 2.21 XP transition thresholds are not verified. " +
        "Changing a displayed level alone would leave dependent progression inconsistent.";

    public bool CanEdit => data.Attributes.Any(a => a.MirrorOffset.HasValue) ||
        data.Points.Any(p => p.Type is "Attribute" or "Primary" or "Espionage");

    public IReadOnlyList<ViewNode> Build(Func<ProgressionBinding, FieldNode> bind)
    {
        var result = new List<ViewNode>();
        result.Add(new StatsNode(data.Proficiencies.Select(p => new Stat(p.Type == "Level" ? "Level" : "Street cred", p.Level.ToString())).ToArray()));
        foreach (var proficiency in data.Proficiencies)
        {
            result.Add(bind(new($"progression/{proficiency.Type}/level", proficiency.Type == "Level" ? "Level" : "Street cred",
                IntegerSpec.Int32, () => FieldValue.Of(proficiency.Level))) with { Warning = TransitionWarning });
            result.Add(bind(new($"progression/{proficiency.Type}/xp", "Current XP", IntegerSpec.Int32,
                () => FieldValue.Of(proficiency.Experience))) with { Warning = TransitionWarning });
            result.Add(bind(new($"progression/{proficiency.Type}/threshold", "Stored XP threshold (0 when omitted)", IntegerSpec.Int32,
                () => FieldValue.Of(proficiency.Threshold))) with { Warning = TransitionWarning });
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
                    : "Updates both development data and the forced player stat. Existing perks and point balances are preserved.",
            });
        }

        foreach (var point in data.Points.Where(p => p.Type is "Attribute" or "Primary" or "Espionage"))
        {
            var maximum = Math.Max(MaximumPointBalance, point.Original);
            var label = point.Type switch { "Attribute" => "Unspent attribute points", "Primary" => "Unspent perk points", _ => "Unspent relic points" };
            result.Add(bind(new($"progression/points/{point.Type}", label, new IntegerSpec(0, maximum),
                () => FieldValue.Of(point.Value), value => point.Value = value.AsInt32(), value => Validate(value, 0, maximum))) with {
                Warning = "Experimental: changes only unspent points. Spent points and unlocks are preserved; the editor cap is a policy.",
            });
        }

        result.Add(new SectionNode("Dependent player stats", data.Attributes.Where(a => a.MirrorOffset.HasValue)
            .Select(a => (ViewNode)bind(new($"stats/{a.Id}", $"{a.Label} forced stat", FloatSpec.Single,
                () => Mirror(a)))).ToArray()) { Collapsed = true });
        return result;
    }

    public IReadOnlyDictionary<string, FieldValue> DerivedValues() => data.Attributes.Where(a => a.MirrorOffset.HasValue)
        .ToDictionary(a => $"stats/{a.Id}", Mirror, StringComparer.Ordinal);

    public void Verify(CsavContainer container, CsavTree tree)
    {
        var reopened = ProgressionData.TryRead(container, tree, out var warning)
            ?? throw new SaveFormatException($"Edited progression failed validation: {warning}");
        if (!reopened.Points.Select(p => (p.Type, p.Value)).SequenceEqual(data.Points.Select(p => (p.Type, p.Value))) ||
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
