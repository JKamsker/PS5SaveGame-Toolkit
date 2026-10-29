using System.Buffers;
using System.IO.Compression;

namespace SaveToolkit.Core.Export;

/// <summary>Bounded ZIP copying; the caller owns and cleans its destination on failure.</summary>
internal static class ExportZipWriter
{
    private const int CopyBufferSize = 81920;

    public static void Write(ExportPlan plan, Stream output, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
            try
            {
                foreach (var file in plan.Included)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = zip.CreateEntry(WorkspaceExporter.ExportPath(file), CompressionLevel.Fastest);
                    using var target = entry.Open();
                    if (plan.Overrides.TryGetValue(file.RelativePath, out var content))
                    {
                        for (var offset = 0; offset < content.Length;)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var count = Math.Min(CopyBufferSize, content.Length - offset);
                            target.Write(content.Span.Slice(offset, count));
                            offset += count;
                        }
                    }
                    else
                    {
                        using var source = file.OpenRead();
                        while (true)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var count = source.Read(buffer, 0, CopyBufferSize);
                            cancellationToken.ThrowIfCancellationRequested();
                            if (count == 0) break;
                            target.Write(buffer, 0, count);
                        }
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
            finally { ArrayPool<byte>.Shared.Return(buffer); }
        }
        // Disposing entries may itself write buffered compressed data to the destination.
        cancellationToken.ThrowIfCancellationRequested();
    }
}
