using System.Globalization;
using System.Text.RegularExpressions;

namespace SaveToolkit.Sdk.Values;

/// <summary>
/// RFC 8259 number grammar and bounded exact comparison. Exponents are never expanded into zeros;
/// work and storage are proportional to lexeme length, even for 1e1000000.
/// </summary>
public static class ExactJsonNumber
{
    /// <summary>Default maximum numeric lexeme length.</summary>
    public const int DefaultMaxLength = 4096;
    /// <summary>Largest supported lexeme-length budget.</summary>
    public const int HardMaxLength = 16384;
    /// <summary>Default maximum absolute exponent written in a lexeme.</summary>
    public const int DefaultMaxAbsoluteExponent = 1_000_000;
    /// <summary>Largest supported absolute-exponent budget.</summary>
    public const int HardMaxAbsoluteExponent = 1_000_000_000;

    private static readonly Regex Grammar = new(
        @"\A-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?\z",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    /// <summary>Returns null for a valid bounded lexeme, or a user-facing grammar, resource-limit or exact-range error.</summary>
    public static string? Validate(string text, ExactNumberSpec? spec = null)
    {
        spec ??= new ExactNumberSpec();
        if (spec.MaxLength is < 1 or > HardMaxLength || spec.MaxAbsoluteExponent is < 0 or > HardMaxAbsoluteExponent)
        {
            return "Invalid exact-number resource limits.";
        }

        var error = ValidateLexeme(text, spec.MaxLength, spec.MaxAbsoluteExponent);
        if (error is not null) return error;
        foreach (var bound in new[] { spec.Min, spec.Max })
        {
            if (bound is not null && ValidateLexeme(bound, HardMaxLength, HardMaxAbsoluteExponent) is not null)
                return "Invalid exact-number range bound.";
        }

        if (spec.Min is { } min && spec.Max is { } max && Compare(min, max) > 0)
            return "Invalid exact-number range.";
        if (spec.Min is { } lower && Compare(text, lower) < 0) return $"Must be at least {lower}.";
        if (spec.Max is { } upper && Compare(text, upper) > 0) return $"Must be at most {upper}.";
        return null;
    }

    private static string? ValidateLexeme(string text, int maxLength, int maxExponent)
    {
        if (text.Length > maxLength) return $"At most {maxLength} characters in a JSON number.";
        if (!Grammar.IsMatch(text)) return "Expected a JSON number (no whitespace, leading +, or leading zeros).";
        var exponentAt = text.IndexOfAny(['e', 'E']);
        if (exponentAt >= 0 && (!int.TryParse(text.AsSpan(exponentAt + 1), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var exponent) || exponent < -maxExponent || exponent > maxExponent))
            return $"Absolute exponent must be at most {maxExponent}.";
        return null;
    }

    // Only call after bounded grammar validation. Strip insignificant zeros for comparison, never storage.
    private static int Compare(string left, string right)
    {
        var a = Parts.Parse(left);
        var b = Parts.Parse(right);
        if (a.Sign != b.Sign) return a.Sign.CompareTo(b.Sign);
        if (a.Sign == 0) return 0;
        var magnitude = a.Order.CompareTo(b.Order);
        if (magnitude == 0)
        {
            var width = Math.Max(a.Digits.Length, b.Digits.Length);
            magnitude = string.CompareOrdinal(a.Digits.PadRight(width, '0'), b.Digits.PadRight(width, '0'));
        }

        return a.Sign * Math.Sign(magnitude);
    }

    private sealed record Parts(int Sign, string Digits, long Order)
    {
        public static Parts Parse(string text)
        {
            var exponentAt = text.IndexOfAny(['e', 'E']);
            var mantissa = exponentAt < 0 ? text : text[..exponentAt];
            var exponent = exponentAt < 0 ? 0 : long.Parse(text.AsSpan(exponentAt + 1), CultureInfo.InvariantCulture);
            var point = mantissa.IndexOf('.');
            var fractionLength = point < 0 ? 0 : mantissa.Length - point - 1;
            var digits = mantissa.TrimStart('-').Replace(".", "", StringComparison.Ordinal).TrimStart('0');
            return new(digits.Length == 0 ? 0 : text[0] == '-' ? -1 : 1, digits, exponent - fractionLength + digits.Length);
        }
    }
}
