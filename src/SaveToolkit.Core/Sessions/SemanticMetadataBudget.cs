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
        if (limit < 0 || !AddString(ref bytes, field.ValueId, limit)
            || !AddString(ref bytes, field.Label, limit)
            || !AddString(ref bytes, field.CanonicalValueId, limit)) return false;
        foreach (var path in field.Path)
            if (!AddString(ref bytes, path, limit)) return false;
        if (field.Target is { } target
            && (!AddString(ref bytes, target.CollectionId, limit)
                || !AddString(ref bytes, target.RowKey, limit)
                || !AddString(ref bytes, target.Query.Cursor, limit))) return false;
        if (field.Spec is { } spec)
        {
            if (!FieldSpecMetadata.TryEstimate(spec, limit - bytes, out var specBytes)) return false;
            bytes += specBytes;
        }
        if (field.Presentation is { } presentation)
        {
            if (!Add(ref bytes, PresentationOverheadBytes, limit)
                || !AddString(ref bytes, presentation.Warning, limit)
                || !AddString(ref bytes, presentation.Description, limit)) return false;
        }
        return AddString(ref bytes, field.Value.ToString(), limit);
    }

    public static bool TryEstimate(FieldMetadata metadata, long limit, out long bytes)
    {
        bytes = FieldOverheadBytes;
        if (limit < 0 || !AddString(ref bytes, metadata.ValueId, limit)
            || !AddString(ref bytes, metadata.Label, limit)
            || !AddString(ref bytes, metadata.CanonicalValueId, limit)) return false;
        if (metadata.Path is { } path)
            foreach (var segment in path)
                if (!AddString(ref bytes, segment, limit)) return false;
        if (!FieldSpecMetadata.TryEstimate(metadata.Spec, limit - bytes, out var specBytes)) return false;
        bytes += specBytes;
        if (metadata.Presentation is { } presentation)
        {
            if (!Add(ref bytes, PresentationOverheadBytes, limit)
                || !AddString(ref bytes, presentation.Warning, limit)
                || !AddString(ref bytes, presentation.Description, limit)) return false;
        }
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
