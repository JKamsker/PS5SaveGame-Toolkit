using SaveToolkit.Core.Storage;

namespace SaveToolkit.Testing.Diff;

/// <summary>
/// A save set imported for diffing. The import copies content into a private folder under the system
/// temp directory (never next to the input), which is deleted on dispose.
/// </summary>
public sealed class LoadedSaveSet : IDisposable
{
    internal LoadedSaveSet(string source, DiskSaveSet set)
    {
        Source = source;
        Set = set;
    }

    public string Source { get; }

    public DiskSaveSet Set { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Set.StorageRoot))
            {
                Directory.Delete(Set.StorageRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup of our own temp copy; a locked file must not hide the diff result.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }
}

/// <summary>
/// Opens a decrypted dump for comparison: a <c>.zip</c> (duplicate entries become generations 0, 1, ...
/// in archive order), a folder (its own name is kept as the first path segment, so a
/// <c>PPSAxxxxx</c> folder is recognised) or a single file. Reuses <see cref="SaveSetImporter"/>, so
/// title/backup/generation detection is identical to the app.
/// </summary>
public static class SaveSetLoader
{
    public static LoadedSaveSet Load(string path, ImportLimits? limits = null)
    {
        var full = System.IO.Path.GetFullPath(path);
        var sources = new List<ImportSource>();
        if (Directory.Exists(full))
        {
            var root = new DirectoryInfo(full);
            foreach (var file in root.EnumerateFiles("*", SearchOption.AllDirectories).OrderBy(f => f.FullName, StringComparer.Ordinal))
            {
                var relative = System.IO.Path.GetRelativePath(root.FullName, file.FullName).Replace('\\', '/');
                var fullName = file.FullName;
                sources.Add(new ImportSource($"{root.Name}/{relative}", () => File.OpenRead(fullName)));
            }
        }
        else if (File.Exists(full))
        {
            sources.Add(new ImportSource(System.IO.Path.GetFileName(full), () => File.OpenRead(full)));
        }
        else
        {
            throw new FileNotFoundException($"Save set not found: {path}", path);
        }

        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SaveToolkit.SaveDiff", Guid.NewGuid().ToString("N"));
        try
        {
            return new LoadedSaveSet(full, SaveSetImporter.Import(sources, temp, limits));
        }
        catch
        {
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }

            throw;
        }
    }
}
