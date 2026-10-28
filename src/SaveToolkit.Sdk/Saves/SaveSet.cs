namespace SaveToolkit.Sdk.Saves;

/// <summary>
/// An imported collection of decrypted PS5 save directories (typically one title dump).
/// The set is read-only; plugins return new file contents through <see cref="ISaveDocument.Serialize"/>.
/// </summary>
public interface ISaveSet
{
    IReadOnlyList<ISaveDirectory> Directories { get; }

    IEnumerable<ISaveFile> AllFiles => Directories.SelectMany(d => d.Files);

    ISaveFile? FindFile(string relativePath) =>
        AllFiles.FirstOrDefault(f => string.Equals(f.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// One PS5 save directory, e.g. <c>PPSA02530/sdimg_SAVESERVICE-LINE-0-0</c>.
/// </summary>
public interface ISaveDirectory
{
    string Id { get; }

    ISaveSet SaveSet { get; }

    /// <summary>Title id such as <c>PPSA02530</c>, taken from the folder layout or param.sfo.</summary>
    string TitleId { get; }

    /// <summary>Logical save directory name without the <c>sdimg_</c> / <c>sdimg_sce_bu_</c> prefix.</summary>
    string Name { get; }

    /// <summary>Folder name exactly as found in the dump.</summary>
    string RawName { get; }

    /// <summary>True for the console-maintained backup copy (<c>sdimg_sce_bu_*</c>).</summary>
    bool IsBackup { get; }

    /// <summary>
    /// Dumps can contain the same path more than once (several snapshots appended to one archive).
    /// The n-th occurrence of a path belongs to generation n, starting at 0.
    /// </summary>
    int Generation { get; }

    ParamSfo? ParamSfo { get; }

    /// <summary>Game files of this directory, excluding <c>sce_sys/*</c>.</summary>
    IReadOnlyList<ISaveFile> Files { get; }

    /// <summary>System files under <c>sce_sys/</c> (param.sfo, keystone, icon0.png).</summary>
    IReadOnlyList<ISaveFile> SystemFiles { get; }
}

/// <summary>A single file inside a save directory. Content is immutable for the lifetime of the set.</summary>
public interface ISaveFile
{
    string Id { get; }

    ISaveDirectory Directory { get; }

    /// <summary>Path relative to the set root using forward slashes, unique within the set.</summary>
    string RelativePath { get; }

    /// <summary>Path relative to its save directory, e.g. <c>data000.bin</c> or <c>sce_sys/param.sfo</c>.</summary>
    string PathInDirectory { get; }

    string FileName { get; }

    long Length { get; }

    Stream OpenRead();

    byte[] ReadAllBytes();

    /// <summary>Reads up to <paramref name="count"/> bytes from the start of the file (cheap probing).</summary>
    byte[] ReadHeader(int count);

    /// <summary>Sibling game file in the same directory, or null.</summary>
    ISaveFile? Sibling(string pathInDirectory) =>
        Directory.Files.FirstOrDefault(f => string.Equals(f.PathInDirectory, pathInDirectory, StringComparison.OrdinalIgnoreCase));
}
