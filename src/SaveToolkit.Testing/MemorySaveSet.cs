using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Testing;

/// <summary>Tiny in-memory save set for unit tests and synthetic probe inputs; owns a copy of each input file.</summary>
public sealed class MemorySaveSet : ISaveSet
{
    private readonly List<ISaveDirectory> _directories = [];

    public IReadOnlyList<ISaveDirectory> Directories => _directories;

    /// <summary>Adds a directory <c>{titleId}/sdimg_{name}</c> with the given files (path in directory → content).</summary>
    public MemorySaveSet With(string titleId, string name, IReadOnlyDictionary<string, byte[]> files, bool backup = false)
    {
        _directories.Add(new Dir(this, $"m{_directories.Count}", titleId, name, backup, files));
        return this;
    }

    public static ISaveFile SingleFile(string fileName, byte[] content, string titleId = "PPSA00000", string directory = "TEST") =>
        new MemorySaveSet().With(titleId, directory, new Dictionary<string, byte[]> { [fileName] = content }).Directories[0].Files[0];

    private sealed class Dir : ISaveDirectory
    {
        public Dir(ISaveSet set, string id, string titleId, string name, bool backup, IReadOnlyDictionary<string, byte[]> files)
        {
            SaveSet = set;
            Id = id;
            TitleId = titleId;
            Name = name;
            IsBackup = backup;
            RawName = (backup ? "sdimg_sce_bu_" : "sdimg_") + name;
            var all = files.Select((kv, i) => (ISaveFile)new MemoryFile(this, $"{id}f{i}", kv.Key, kv.Value.ToArray())).ToList();
            Files = all.Where(f => !f.PathInDirectory.StartsWith("sce_sys/", StringComparison.OrdinalIgnoreCase)).ToList();
            SystemFiles = all.Except(Files).ToList();
            ParamSfo = files.TryGetValue("sce_sys/param.sfo", out var sfo) ? ParamSfo.TryParse(sfo) : null;
        }

        public string Id { get; }
        public ISaveSet SaveSet { get; }
        public string TitleId { get; }
        public string Name { get; }
        public string RawName { get; }
        public bool IsBackup { get; }
        public int Generation => 0;
        public ParamSfo? ParamSfo { get; }
        public IReadOnlyList<ISaveFile> Files { get; }
        public IReadOnlyList<ISaveFile> SystemFiles { get; }
    }

    private sealed class MemoryFile(Dir dir, string id, string path, byte[] content) : ISaveFile
    {
        public string Id => id;
        public ISaveDirectory Directory => dir;
        public string RelativePath => $"{dir.TitleId}/{dir.RawName}/{path}";
        public string PathInDirectory => path;
        public string FileName => path[(path.LastIndexOf('/') + 1)..];
        public long Length => content.Length;
        public Stream OpenRead() => new MemoryStream(content, writable: false);
        public byte[] ReadAllBytes() => (byte[])content.Clone();
        public byte[] ReadHeader(int count) => content[..Math.Min(count, content.Length)];
    }
}
