using System.Buffers.Binary;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.SuckerPunch;

/// <summary>A fixed-width timer edit; no difficulty strings, per-difficulty counters or metadata are rewritten.</summary>
internal sealed class TsushimaPlayTime(byte[] bytes, int offset, float originalSeconds)
{
    public const string ValueId = "progress/play-seconds";
    // Highest observed v48/v49 timer in the supplied corpus; this is an editor policy, not a game limit.
    private const float HighestObservedSeconds = 198_318.828125f;
    public const string Warning = "Not validated on console. Based on v48/v49 timer/subtitle correlations. " +
        "Only the timer and container checksum change; per-difficulty counters and param.sfo SUBTITLE remain unchanged and may disagree. " +
        "The game may normalize or reject the edited save. The upper bound is the observed range or the original value, not a game limit.";

    public FloatSpec Spec => FloatSpec.Single with { Min = 0, Max = Math.Max(HighestObservedSeconds, originalSeconds), Unit = "seconds" };
    public FieldValue Read() => FieldValue.Of(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(offset)));
    public void Write(FieldValue value) => BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(offset), (float)value.AsDouble());

    public string? Validate(FieldValue value)
    {
        if (value.Kind is not (FieldValueKind.Float or FieldValueKind.Integer))
        {
            return "Play time must be a finite number of seconds.";
        }

        var seconds = value.AsDouble();
        return !double.IsFinite(seconds) || seconds < 0 || seconds > Spec.Max
            ? $"Play time must be between 0 and {Spec.Max} seconds." : null;
    }
}
