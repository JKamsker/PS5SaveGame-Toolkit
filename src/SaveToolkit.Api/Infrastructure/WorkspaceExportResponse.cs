using SaveToolkit.Core.Export;

namespace SaveToolkit.Api.Infrastructure;

/// <summary>Spools a complete ZIP to disk so managed memory does not grow with the archive.</summary>
internal static class WorkspaceExportResponse
{
    internal static IResult Create(ExportPlan plan, string downloadName)
    {
        var path = Path.Combine(Path.GetTempPath(), $"SaveToolkit-export-{Guid.NewGuid():N}.zip");
        var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose,
        });
        try
        {
            // Finish compression before headers are sent, preserving clean error responses and
            // Content-Length. ZIP entry disposal still writes synchronously in .NET 10, so it
            // cannot target the HTTP response directly. File results copy asynchronously and
            // dispose the stream (deleting its scratch file), including aborted transfers.
            WorkspaceExporter.WriteZip(plan, stream);
            stream.Position = 0;
            return Results.File(stream, "application/zip", downloadName);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}
