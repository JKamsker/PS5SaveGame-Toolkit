namespace SaveToolkit.Sdk.Saves;

/// <summary>
/// An imported collection of decrypted PS5 save directories (typically one title dump).
/// The set is read-only; plugins return new file contents through <see cref="ISaveDocument.Serialize"/>.
/// </summary>
public interface ISaveSet
{
    /// <summary>Imported save directories, including backups and repeated-path generations.</summary>
    IReadOnlyList<ISaveDirectory> Directories { get; }

    /// <summary>Game files across directories; system metadata is available separately through each directory.</summary>
    IEnumerable<ISaveFile> AllFiles => Directories.SelectMany(d => d.Files);

    /// <summary>Finds a game file by save-set relative path, ignoring case; returns null when absent.</summary>
    ISaveFile? FindFile(string relativePath) =>
        AllFiles.FirstOrDefault(f => string.Equals(f.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds any file, including system metadata, by save-set relative path.</summary>
    ISaveFile? FindAnyFile(string relativePath) =>
        Directories.SelectMany(d => d.Files.Concat(d.SystemFiles))
            .FirstOrDefault(f => string.Equals(f.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// One PS5 save directory, e.g. <c>PPSA02530/sdimg_SAVESERVICE-LINE-0-0</c>.
/// </summary>
public interface ISaveDirectory
{
    /// <summary>Opaque identity assigned by the host for this imported directory.</summary>
    string Id { get; }

    /// <summary>The immutable imported set that contains this directory and its siblings.</summary>
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

    /// <summary>Optional parsed display metadata; null when absent, invalid or too large to parse.</summary>
    ParamSfo? ParamSfo { get; }

    /// <summary>Game files of this directory, excluding <c>sce_sys/*</c>.</summary>
    IReadOnlyList<ISaveFile> Files { get; }

    /// <summary>System files under <c>sce_sys/</c> (param.sfo, keystone, icon0.png).</summary>
    IReadOnlyList<ISaveFile> SystemFiles { get; }

    /// <summary>Finds a game or system file by path relative to this directory.</summary>
    ISaveFile? FindAnyFile(string pathInDirectory) =>
        Files.Concat(SystemFiles).FirstOrDefault(f =>
            string.Equals(f.PathInDirectory, pathInDirectory, StringComparison.OrdinalIgnoreCase));
}

/// <summary>A single file inside a save directory. Content is immutable for the lifetime of the set.</summary>
public interface ISaveFile
{
    /// <summary>Opaque identity assigned by the host for this imported file.</summary>
    string Id { get; }

    /// <summary>The containing save directory and its metadata.</summary>
    ISaveDirectory Directory { get; }

    /// <summary>Path relative to the set root using forward slashes, unique within the set.</summary>
    string RelativePath { get; }

    /// <summary>Path relative to its save directory, e.g. <c>data000.bin</c> or <c>sce_sys/param.sfo</c>.</summary>
    string PathInDirectory { get; }

    /// <summary>Final path component, excluding parent directories.</summary>
    string FileName { get; }

    /// <summary>Immutable content length in bytes.</summary>
    long Length { get; }

    /// <summary>Opens an independent read stream. The caller must dispose it.</summary>
    Stream OpenRead();

    /// <summary>Reads the entire content into a new array; bound file size before parsing large inputs.</summary>
    byte[] ReadAllBytes();

    /// <summary>Reads up to <paramref name="count"/> bytes from the start of the file (cheap probing).</summary>
    byte[] ReadHeader(int count);

    /// <summary>Sibling game file in the same directory, or null.</summary>
    ISaveFile? Sibling(string pathInDirectory) =>
        Directory.Files.FirstOrDefault(f => string.Equals(f.PathInDirectory, pathInDirectory, StringComparison.OrdinalIgnoreCase));
}
