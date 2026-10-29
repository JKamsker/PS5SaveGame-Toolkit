using SaveToolkit.Sdk;
using SaveToolkit.Core.Sessions.Groups;

namespace SaveToolkit.Core.Sessions;

/// <summary>Copies plugin-owned output while retaining cancellation boundaries between bounded chunks.</summary>
internal static class DocumentOutputSnapshot
{
    private const int CopyChunkBytes = 81920;

    public static IReadOnlyList<FileOutput> Capture(ISaveDocument document, CancellationToken cancellationToken)
        => Copy(Read(document, cancellationToken), cancellationToken);

    // A failed/oversized optional proof must not prevent an otherwise valid edit.
    internal static IReadOnlyList<FileOutput>? TryCapture(ISaveDocument document, long maxBytes, CancellationToken cancellationToken)
    {
        var outputs = Read(document, cancellationToken);
        return outputs.Sum(output => (long)output.Content.Length) <= maxBytes ? Copy(outputs, cancellationToken) : null;
    }

    private static IReadOnlyList<FileOutput> Read(ISaveDocument document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return document is StagedGroupDocument group
            ? group.SerializeForSnapshot(cancellationToken) : document.Serialize();
    }

    private static IReadOnlyList<FileOutput> Copy(IReadOnlyList<FileOutput> outputs, CancellationToken cancellationToken)
    {
        var snapshot = new List<FileOutput>(outputs.Count);
        foreach (var output in outputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = new byte[output.Content.Length];
            for (var offset = 0; offset < content.Length;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var length = Math.Min(CopyChunkBytes, content.Length - offset);
                output.Content.Span.Slice(offset, length).CopyTo(content.AsSpan(offset, length));
                offset += length;
            }
            snapshot.Add(output with { Content = content });
        }
        cancellationToken.ThrowIfCancellationRequested();
        return snapshot;
    }
}
