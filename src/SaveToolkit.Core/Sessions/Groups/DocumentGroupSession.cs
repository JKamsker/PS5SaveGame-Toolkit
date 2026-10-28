using System.Security.Cryptography;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Sessions.Groups;

public sealed record GroupMemberInfo(string Role, string FileId, string RelativePath, string Sha256, bool IsBackup);

/// <summary>An exclusive path owner. Its ordinary session supplies one history and revision for all members.</summary>
public sealed class DocumentGroupSession
{
    private readonly DiskSaveSet _set;
    private readonly IGamePlugin _plugin;
    private readonly IDocumentGroupPlugin _groupPlugin;
    private readonly ReferenceContext _references;

    internal DocumentGroupSession(string id, string title, DiskSaveSet set, IGamePlugin plugin,
        IReadOnlyList<GroupMemberInfo> members, ReferenceContext references, DocumentSessionOptions? options = null)
    {
        Title = title;
        Members = members;
        _set = set;
        _plugin = plugin;
        _groupPlugin = (IDocumentGroupPlugin)plugin;
        _references = references;
        var limits = options ?? DocumentSessionOptions.Default;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaxHistory);
        ArgumentOutOfRangeException.ThrowIfNegative(limits.MaxSnapshotBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limits.MaxQueryActionRows);
        CheckFingerprints();
        // Freeze selected contents once. Overlay keeps all other files and directory metadata intact.
        var frozen = members.ToDictionary(m => m.RelativePath,
            m => (ReadOnlyMemory<byte>)set.FindById(m.FileId)!.ReadAllBytes(), StringComparer.OrdinalIgnoreCase);
        foreach (var member in members)
            if (Convert.ToHexStringLower(SHA256.HashData(frozen[member.RelativePath].Span)) != member.Sha256)
                throw new InvalidOperationException("A group input changed while it was being captured. Reimport the dump.");
        var input = Input(new OverlaySaveSet(set, frozen));
        IGroupedSaveDocument Open() => _groupPlugin.OpenGroup(input, references);
        var staged = new StagedGroupDocument(Open, Verify, CheckFingerprints, limits.MaxSnapshotBytes);
        Session = new DocumentSession(id, set.FindById(members[0].FileId)!, plugin, staged,
            options: limits, openOriginal: Open, references: references);
    }

    public string Title { get; }
    public IReadOnlyList<GroupMemberInfo> Members { get; }
    public DocumentSession Session { get; }
    internal ReferenceContext References => _references;

    public bool Owns(string relativePath) => Members.Any(m => string.Equals(m.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase));

    public void CheckFingerprints()
    {
        foreach (var member in Members)
        {
            using var stream = _set.FindById(member.FileId)!.OpenRead();
            var actual = Convert.ToHexStringLower(SHA256.HashData(stream));
            if (!string.Equals(actual, member.Sha256, StringComparison.Ordinal))
                throw new InvalidOperationException($"Group input '{member.RelativePath}' changed since selection. Close and reimport the dump.");
        }
    }

    /// <summary>Validate all outputs together, then reopen each in its own file role.</summary>
    internal IReadOnlyList<FileOutput> Verify(IGroupedSaveDocument document)
    {
        CheckFingerprints();
        var outputs = CopyOutputs(document.Serialize());
        VerifyOutputs(outputs);
        return outputs.Select(o => new FileOutput(o.Key, o.Value)).ToArray();
    }

    internal void VerifyOutputs(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> outputs, ISaveSet? finalSet = null)
    {
        CheckFingerprints();
        if (outputs.Count != Members.Count || outputs.Keys.Any(path => !Owns(path)))
            throw new SaveFormatException("A document group must serialize every owned member exactly once and cannot write unowned files.");
        var overlay = new OverlaySaveSet(finalSet ?? _set, outputs);
        var errors = _groupPlugin.ValidateGroup(Input(overlay), _references);
        if (errors.Count > 0) throw new SaveFormatException(string.Join("; ", errors.Select(e => e.Message)));
        foreach (var member in Members)
        {
            var file = ((ISaveSet)overlay).FindFile(member.RelativePath)!;
            using var reopened = _plugin.Open(file, _references);
            _ = reopened.BuildView();
        }
    }

    internal static Dictionary<string, ReadOnlyMemory<byte>> CopyOutputs(IReadOnlyList<FileOutput> outputs)
    {
        var copy = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.OrdinalIgnoreCase);
        foreach (var output in outputs)
            if (!copy.TryAdd(output.RelativePath, output.Content.ToArray()))
                throw new SaveFormatException($"The group returned duplicate output '{output.RelativePath}'.");
        return copy;
    }

    private DocumentGroupInput Input(ISaveSet set) => new(Array.AsReadOnly(Members.Select(m =>
        new DocumentGroupFile(m.Role, set.FindFile(m.RelativePath)!)).ToArray()));
}
