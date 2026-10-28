using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace SaveToolkit.Testing.Diff;

/// <summary>
/// Minimal case-insensitive glob used by churn rules, fixture expectations and file filters:
/// <c>*</c> matches any run of characters (including <c>/</c>), <c>?</c> matches exactly one.
/// </summary>
public static class Glob
{
    private static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    public static bool IsMatch(string pattern, string text) => Cache.GetOrAdd(pattern, Build).IsMatch(text);

    private static Regex Build(string pattern)
    {
        var regex = new StringBuilder("^");
        foreach (var c in pattern)
        {
            regex.Append(c switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(c.ToString()),
            });
        }

        regex.Append('$');
        // Patterns come from manifests; a timeout keeps a pathological pattern from hanging the tool.
        return new Regex(regex.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromSeconds(1));
    }
}
