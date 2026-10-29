using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Storage;

/// <summary>
/// Read-only view of a save set with some files replaced by new content. Used to re-open freshly
/// serialised output with the plugin before anything is handed to the user.
/// </summary>
public sealed class OverlaySaveSet : ISaveSet
{
    private readonly List<OverlayDirectory> _directories;

    public OverlaySaveSet(ISaveSet inner, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> overrides)
        : this(inner, overrides, CancellationToken.None) { }

    internal OverlaySaveSet(ISaveSet inner, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> overrides,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _directories = new List<OverlayDirectory>(inner.Directories.Count);
        foreach (var directory in inner.Directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _directories.Add(new OverlayDirectory(this, directory, overrides, cancellationToken));
        }
    }

    public IReadOnlyList<ISaveDirectory> Directories => _directories;

    private sealed class OverlayDirectory : ISaveDirectory
    {
        private readonly ISaveDirectory _inner;

        public OverlayDirectory(OverlaySaveSet set, ISaveDirectory inner, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> overrides,
            CancellationToken cancellationToken)
        {
            SaveSet = set;
            _inner = inner;
            Files = WrapFiles(inner.Files, overrides, cancellationToken);
            SystemFiles = WrapFiles(inner.SystemFiles, overrides, cancellationToken);
        }

        public string Id => _inner.Id;
        public ISaveSet SaveSet { get; }
        public string TitleId => _inner.TitleId;
        public string Name => _inner.Name;
        public string RawName => _inner.RawName;
        public bool IsBackup => _inner.IsBackup;
        public int Generation => _inner.Generation;
        public ParamSfo? ParamSfo => _inner.ParamSfo;
        public IReadOnlyList<ISaveFile> Files { get; }
        public IReadOnlyList<ISaveFile> SystemFiles { get; }

        private List<ISaveFile> WrapFiles(IReadOnlyList<ISaveFile> files,
            IReadOnlyDictionary<string, ReadOnlyMemory<byte>> overrides, CancellationToken cancellationToken)
        {
            var wrapped = new List<ISaveFile>(files.Count);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                wrapped.Add(Wrap(file, overrides));
            }
            return wrapped;
        }

        // The explicit nullable type matters: `? content : null` would type the conditional as
        // ReadOnlyMemory<byte> and turn null into an empty override, hiding every untouched file.
        private OverlayFile Wrap(ISaveFile file, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> overrides) =>
            new(this, file, overrides.TryGetValue(file.RelativePath, out var content) ? content : (ReadOnlyMemory<byte>?)null);
    }

    private sealed class OverlayFile(ISaveDirectory directory, ISaveFile inner, ReadOnlyMemory<byte>? content) : ISaveFile
    {
        public string Id => inner.Id;
        public ISaveDirectory Directory => directory;
        public string RelativePath => inner.RelativePath;
        public string PathInDirectory => inner.PathInDirectory;
        public string FileName => inner.FileName;
        public long Length => content?.Length ?? inner.Length;

        public Stream OpenRead() => content is { } c ? new MemoryStream(c.ToArray(), writable: false) : inner.OpenRead();

        public byte[] ReadAllBytes() => content?.ToArray() ?? inner.ReadAllBytes();

        public byte[] ReadHeader(int count) =>
            content is { } c ? c[..Math.Min(count, c.Length)].ToArray() : inner.ReadHeader(count);
    }
}
