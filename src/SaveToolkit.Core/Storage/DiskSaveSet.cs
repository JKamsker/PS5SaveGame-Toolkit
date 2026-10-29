using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Storage;

/// <summary>Save set whose file contents live in a private blob folder on disk.</summary>
public sealed class DiskSaveSet : ISaveSet
{
    private readonly List<DiskSaveDirectory> _directories = [];
    private readonly Dictionary<string, DiskSaveFile> _filesById = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ISaveFile> _gameFilesByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ISaveFile> _allFilesByPath = new(StringComparer.OrdinalIgnoreCase);

    internal DiskSaveSet(string storageRoot) => StorageRoot = storageRoot;

    public string StorageRoot { get; }

    public IReadOnlyList<ISaveDirectory> Directories => _directories;

    public IEnumerable<DiskSaveFile> AllDiskFiles => _filesById.Values;

    public DiskSaveFile? FindById(string fileId) => _filesById.GetValueOrDefault(fileId);

    /// <summary>Indexed lookup with the same game-file-only semantics as <see cref="ISaveSet.FindFile"/>.</summary>
    public ISaveFile? FindFile(string relativePath) => _gameFilesByPath.GetValueOrDefault(relativePath);

    public ISaveFile? FindAnyFile(string relativePath) => _allFilesByPath.GetValueOrDefault(relativePath);

    public DiskSaveDirectory? FindDirectory(string directoryId) => _directories.FirstOrDefault(d => d.Id == directoryId);

    /// <summary>The console backup twin (<c>sdimg_sce_bu_*</c>) of a primary directory, or the reverse.</summary>
    public DiskSaveDirectory? Twin(ISaveDirectory directory) => Twin(directory, CancellationToken.None);

    internal DiskSaveDirectory? Twin(ISaveDirectory directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A loose SAVE folder is independent of the console's sdimg_SAVE, even with identical bytes.
        if (!DiskSaveDirectory.IsConsoleSave(directory.RawName)) return null;
        foreach (var candidate in _directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate != directory
                && DiskSaveDirectory.IsConsoleSave(candidate.RawName)
                && candidate.IsBackup != directory.IsBackup
                && candidate.Generation == directory.Generation
                && string.Equals(candidate.TitleId, directory.TitleId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.Name, directory.Name, StringComparison.Ordinal)) return candidate;
        }
        return null;
    }

    internal void Add(DiskSaveDirectory directory)
    {
        _directories.Add(directory);
        foreach (var f in directory.Files.Concat(directory.SystemFiles).Cast<DiskSaveFile>())
        {
            _filesById.Add(f.Id, f);
            _allFilesByPath.TryAdd(f.RelativePath, f);
            if (!f.IsSystemFile) _gameFilesByPath.TryAdd(f.RelativePath, f);
        }
    }
}

public sealed class DiskSaveDirectory : ISaveDirectory
{
    private const string PrimaryPrefix = "sdimg_";

    internal DiskSaveDirectory(DiskSaveSet set, string id, string titleId, string rawName, int generation)
    {
        SaveSet = set;
        Id = id;
        TitleId = titleId;
        RawName = rawName;
        Generation = generation;
        (Name, IsBackup) = SplitRawName(rawName);
    }

    public string Id { get; }
    public ISaveSet SaveSet { get; }
    public string TitleId { get; }
    public string Name { get; }
    public string RawName { get; }
    public bool IsBackup { get; }
    public int Generation { get; }
    public ParamSfo? ParamSfo { get; internal set; }

    public IReadOnlyList<ISaveFile> Files => FileList;
    public IReadOnlyList<ISaveFile> SystemFiles => SystemFileList;

    internal List<ISaveFile> FileList { get; } = [];
    internal List<ISaveFile> SystemFileList { get; } = [];

    /// <summary>Path prefix shared by all files of this directory inside the set.</summary>
    public string RelativeRoot => (Generation > 0 ? $"gen{Generation}/" : string.Empty) + $"{TitleId}/{RawName}";

    internal static bool IsConsoleSave(string rawName) => rawName.StartsWith(PrimaryPrefix, StringComparison.OrdinalIgnoreCase);

    internal static (string Name, bool IsBackup) SplitRawName(string rawName)
    {
        const string backup = "sdimg_sce_bu_";
        if (rawName.StartsWith(backup, StringComparison.OrdinalIgnoreCase))
        {
            return (rawName[backup.Length..], true);
        }

        return IsConsoleSave(rawName)
            ? (rawName[PrimaryPrefix.Length..], false)
            : (rawName, false);
    }
}

public sealed class DiskSaveFile : ISaveFile
{
    internal DiskSaveFile(DiskSaveDirectory directory, string id, string pathInDirectory, string blobPath, long length, string sha256,
        string? importedRelativePath)
    {
        Directory = directory;
        Id = id;
        PathInDirectory = pathInDirectory;
        BlobPath = blobPath;
        Length = length;
        Sha256 = sha256;
        ExportRelativePath = importedRelativePath ?? RelativePath;
    }

    public string Id { get; }
    public ISaveDirectory Directory { get; }
    public string PathInDirectory { get; }
    public string RelativePath => $"{((DiskSaveDirectory)Directory).RelativeRoot}/{PathInDirectory}";
    /// <summary>Source layout for title-less imports; otherwise the established title/generation layout.</summary>
    internal string ExportRelativePath { get; }
    public string FileName => PathInDirectory[(PathInDirectory.LastIndexOf('/') + 1)..];
    public long Length { get; }

    /// <summary>Hex SHA-256 of the original content.</summary>
    public string Sha256 { get; }

    internal string BlobPath { get; }

    public bool IsSystemFile => PathInDirectory.StartsWith("sce_sys/", StringComparison.OrdinalIgnoreCase);

    public Stream OpenRead() => new FileStream(BlobPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);

    public byte[] ReadAllBytes() => File.ReadAllBytes(BlobPath);

    public byte[] ReadHeader(int count)
    {
        using var stream = OpenRead();
        var buffer = new byte[(int)Math.Min(count, Length)];
        stream.ReadExactly(buffer);
        return buffer;
    }
}
