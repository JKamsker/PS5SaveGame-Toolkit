using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Gearbox;

internal static class GearboxSpatialText
{
    public static string Limit(string text)
    {
        if (text.Length <= SpatialViewLimits.MaxTextLength) return text;
        var end = SpatialViewLimits.MaxTextLength - 1;
        if (char.IsHighSurrogate(text[end - 1])) end--;
        return text[..end] + "…";
    }
}
