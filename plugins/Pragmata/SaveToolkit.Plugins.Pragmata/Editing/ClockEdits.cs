using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Pragmata.Editing;

internal static class ClockEdits
{
    public static string? Reconcile(SaveLayout layout, byte[] plaintext, Dictionary<SaveNode, FieldValue> proposed)
    {
        if (layout.Header is null) return null;
        var playtime = layout.Header.Find(KnownHashes.Playtime)!;
        var elapsed = layout.Timers[0];
        var demo = layout.Timers[1];
        var pause = layout.Timers[2];
        if (!proposed.ContainsKey(playtime) && !layout.Timers.Any(proposed.ContainsKey)) return null;
        Int128 Read(SaveNode node) => proposed.TryGetValue(node, out var value) ? value.AsInteger() : PrimitiveCodec.Read(node, plaintext).AsInteger();
        var deductions = Read(demo) + Read(pause);
        if (proposed.ContainsKey(playtime) && !proposed.ContainsKey(elapsed))
        {
            var total = Read(playtime) + deductions;
            if (total > ulong.MaxValue) return "Playtime plus demo and pause time overflows the elapsed clock.";
            proposed[elapsed] = FieldValue.Of(total);
        }
        else
        {
            var actual = Read(elapsed) - deductions;
            if (actual < 0) return "Elapsed time must be at least demo time plus pause time.";
            if (proposed.ContainsKey(playtime) && Read(playtime) != actual)
                return "Playtime and clock edits conflict: playtime must equal elapsed minus demo minus pause.";
            proposed[playtime] = FieldValue.Of(actual);
        }
        return null;
    }
}
