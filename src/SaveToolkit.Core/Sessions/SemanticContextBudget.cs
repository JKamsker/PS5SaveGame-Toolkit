using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Sessions;

internal static class SemanticContextBudget
{
    internal const int MaxStringLength = 4096;
    internal const int MaxBatchCount = FieldMetadataLimits.MaxBatchSize;
    internal const long MaxResponseBytes = 256 * 1024;

    public static bool TryEstimate(SemanticMemberContext context, out long bytes)
    {
        var total = 64L;
        var valid = Add(context.Role) && Add(context.RelativePath) && Add(context.GroupTitle) && Add(context.Provenance);
        bytes = total;
        return valid;

        bool Add(string? value)
        {
            if (value is null || value.Length > MaxStringLength) return value is null;
            var size = Encoding.UTF8.GetByteCount(value);
            if (size > MaxResponseBytes - total) return false;
            total += size;
            return true;
        }
    }
}

internal sealed class SemanticContextResolutionException(
    IReadOnlyDictionary<string, SemanticMemberContext> contexts, string message) : Exception(message)
{
    public IReadOnlyDictionary<string, SemanticMemberContext> Contexts { get; } = contexts;
}
