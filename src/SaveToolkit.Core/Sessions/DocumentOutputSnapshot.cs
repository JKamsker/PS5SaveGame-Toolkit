using SaveToolkit.Sdk;
using SaveToolkit.Core.Sessions.Groups;

namespace SaveToolkit.Core.Sessions;

/// <summary>Copies plugin-owned output while retaining cancellation boundaries between bounded chunks.</summary>
internal static class DocumentOutputSnapshot
{
    private const int CopyChunkBytes = 81920;

    public static IReadOnlyList<FileOutput> Capture(ISaveDocument document, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var outputs = document is StagedGroupDocument group
            ? group.SerializeForSnapshot(cancellationToken) : document.Serialize();
        var snapshot = new List<FileOutput>(outputs.Count);
        foreach (var output in outputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = new byte[output.Content.Length];
            for (var offset = 0; offset < content.Length; offset += Math.Min(CopyChunkBytes, content.Length - offset))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var length = Math.Min(CopyChunkBytes, content.Length - offset);
                output.Content.Span.Slice(offset, length).CopyTo(content.AsSpan(offset, length));
            }
            snapshot.Add(output with { Content = content });
        }
        cancellationToken.ThrowIfCancellationRequested();
        return snapshot;
    }
}
