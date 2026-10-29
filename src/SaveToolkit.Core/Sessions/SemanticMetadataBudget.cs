using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Sessions;

/// <summary>Shared bounded size accounting for retained semantic metadata.</summary>
internal static class SemanticMetadataBudget
{
    private const long FieldOverheadBytes = 256;
    private const long PresentationOverheadBytes = 48;

    public static bool TryEstimate(SemanticField field, long limit, out long bytes)
    {
        bytes = FieldOverheadBytes;
        if (limit < 0 || !AddStrings(ref bytes, limit, field.ValueId, field.Label, field.CanonicalValueId)) return false;
        foreach (var path in field.Path)
            if (!AddString(ref bytes, path, limit)) return false;
        if (field.Target is { } target && !AddStrings(ref bytes, limit,
                target.CollectionId, target.RowKey, target.Query.Cursor)) return false;
        if (field.Spec is { } spec)
        {
            if (!FieldSpecMetadata.TryEstimate(spec, limit - bytes, out var specBytes)) return false;
            bytes += specBytes;
        }
        if (field.Presentation is { } presentation)
        {
            if (!Add(ref bytes, PresentationOverheadBytes, limit)
                || !AddStrings(ref bytes, limit, presentation.Warning, presentation.Description)) return false;
        }
        return AddString(ref bytes, field.Value.ToString(), limit);
    }

    public static bool TryEstimate(FieldMetadata metadata, long limit, out long bytes)
    {
        bytes = FieldOverheadBytes;
        if (limit < 0 || !AddStrings(ref bytes, limit, metadata.ValueId, metadata.Label, metadata.CanonicalValueId)) return false;
        if (metadata.Path is { } path)
            foreach (var segment in path)
                if (!AddString(ref bytes, segment, limit)) return false;
        if (!FieldSpecMetadata.TryEstimate(metadata.Spec, limit - bytes, out var specBytes)) return false;
        bytes += specBytes;
        if (metadata.Presentation is { } presentation)
        {
            if (!Add(ref bytes, PresentationOverheadBytes, limit)
                || !AddStrings(ref bytes, limit, presentation.Warning, presentation.Description)) return false;
        }
        return true;
    }

    private static bool AddStrings(ref long total, long limit, string? first, params string?[] rest)
    {
        if (!AddString(ref total, first, limit)) return false;
        foreach (var value in rest)
            if (!AddString(ref total, value, limit)) return false;
        return true;
    }

    private static bool AddString(ref long total, string? value, long limit)
    {
        if (value is null) return true;
        return value.Length <= (limit - total) / 2 && Add(ref total, 2L * value.Length, limit);
    }

    private static bool Add(ref long total, long value, long limit)
    {
        if (value < 0 || total > limit - value) return false;
        total += value;
        return true;
    }
}
