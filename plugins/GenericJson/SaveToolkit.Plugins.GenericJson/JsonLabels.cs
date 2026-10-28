using System.Text;

namespace SaveToolkit.Plugins.GenericJson;

/// <summary>Original word splitting, not a game-name or localization dictionary.</summary>
internal static class JsonLabels
{
    public static string Friendly(string raw)
    {
        if (raw.Length == 0) return "(empty name)";
        var label = new StringBuilder();
        for (var i = 0; i < raw.Length; i++)
        {
            var current = raw[i];
            if (current is '_' or '/' or '.') current = ' ';
            if (char.IsUpper(current) && i > 0 && (char.IsLower(raw[i - 1])
                || char.IsUpper(raw[i - 1]) && i + 1 < raw.Length && char.IsLower(raw[i + 1]))) label.Append(' ');
            label.Append(current);
        }
        var result = label.ToString().Trim();
        return result.Length == 0 ? raw : char.ToUpperInvariant(result[0]) + result[1..];
    }
}
