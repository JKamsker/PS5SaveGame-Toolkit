using System.Text;

namespace SaveToolkit.Core.Sessions;

/// <summary>Bounds provider-controlled text retained by semantic review diagnostics.</summary>
internal static class SemanticDiagnostics
{
    internal const int MaxMessageChars = 512;
    private const int MaxAggregateConstituents = 8;

    public static string Exception(string prefix, Exception failure) =>
        failure is AggregateException aggregate
            ? Compose(prefix, ": ", Aggregate(aggregate))
            : Compose(prefix, ": ", Bound(failure.Message));

    public static string WithIdentity(string prefix, string identity, string suffix = "") =>
        Compose(prefix, identity, suffix);

    public static string Compose(params string?[] parts)
    {
        var builder = new StringBuilder(MaxMessageChars);
        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part) || builder.Length == MaxMessageChars) continue;
            var remaining = MaxMessageChars - builder.Length;
            var bounded = Truncate(part, remaining);
            builder.Append(bounded);
        }

        return builder.ToString();
    }

    public static string Bound(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return Truncate(message, MaxMessageChars);
    }

    private static string Aggregate(AggregateException aggregate)
    {
        var parts = new List<string>(Math.Min(MaxAggregateConstituents, aggregate.InnerExceptions.Count));
        foreach (var inner in aggregate.InnerExceptions.Take(MaxAggregateConstituents))
            parts.Add(Bound(inner is AggregateException nested ? Aggregate(nested) : inner.Message));
        return Compose(parts.ToArray());
    }

    private static string Truncate(string value, int limit)
    {
        if (value.Length <= limit) return value;
        if (limit <= 1) return limit == 0 ? string.Empty : "…";
        var length = limit - 1;
        if (length > 0 && char.IsHighSurrogate(value[length - 1])) length--;
        return value[..length] + '…';
    }
}
