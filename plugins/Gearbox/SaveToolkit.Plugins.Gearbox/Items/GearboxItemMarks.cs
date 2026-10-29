using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Existing item flag policy shared by scalar and batch writes.</summary>
internal static class GearboxItemMarks
{
    internal const long Mask = 6;
    private const long Favourite = 2;
    private const long Junk = 4;

    internal static bool Supported(ProtoMessage message) => (message.Integer("flags") & Mask) != Mask;
    internal static int Get(ProtoMessage message) => (message.Integer("flags") & Mask) switch
    { Favourite => 1, Junk => 2, _ => 0 };

    internal static long Flags(long current, int mark) => (current & ~Mask) | (mark switch
    { 0 => 0, 1 => Favourite, 2 => Junk, _ => throw new SaveFormatException("Choose ordinary, favourite or junk.") });

    internal static void Set(ProtoMessage message, int mark)
    {
        if (!Supported(message)) throw new SaveFormatException("Conflicting favourite and junk flags are preserved.");
        message.SetInteger("flags", Flags(message.Integer("flags"), mark));
    }
}
