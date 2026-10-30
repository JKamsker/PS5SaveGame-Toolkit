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
    private DocumentSession? _session;
    private IReadOnlyList<string> _declaredOutputPaths = [];

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
        var frozen = FreezeMembers(members, set.FindById, CancellationToken.None);
        foreach (var member in members)
            if (Convert.ToHexStringLower(SHA256.HashData(frozen[member.RelativePath].Span)) != member.Sha256)
                throw new InvalidOperationException("A group input changed while it was being captured. Reimport the dump.");
        var input = Input(new OverlaySaveSet(set, frozen));
        IGroupedSaveDocument Open()
        {
            // Targeted semantic capture must be able to resolve explicit metadata before any
            // legacy view/lazy walk. Implementations publish requested bindings from the
            // document model; the session builds a view only after that bounded phase.
            return _groupPlugin.OpenGroup(input, references);
        }
        var declarationProbe = Open();
        try
        {
            _declaredOutputPaths = OutputOwnershipPolicy.Resolve(set.FindById(members[0].FileId)!, declarationProbe);
            var completed = declarationProbe;
            declarationProbe = null!;
            completed.Dispose();
        }
        catch (Exception failure)
        {
            SessionCleanup.DisposeAfterFailure(declarationProbe, failure, "Group declaration probing and cleanup failed.");
            throw;
        }
        var staged = new StagedGroupDocument(Open, Verify, token => CheckFingerprints(token), limits.MaxSnapshotBytes);
        DocumentSession? created = null;
        try
        {
            created = new DocumentSession(id, set.FindById(members[0].FileId)!, plugin, staged,
                options: limits, openOriginal: Open, references: references,
                memberContext: ids => ResolveFieldContext(staged, ids));
            _session = created;
            CheckFingerprints();
            Session = created;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            try
            {
                if (created is not null) created.Dispose();
                else staged.Dispose();
            }
            catch (Exception cleanup) when (cleanup is not OutOfMemoryException)
            {
                throw new AggregateException("Group construction and cleanup failed.", failure, cleanup);
            }
            throw;
        }
    }

    public string Title { get; }
    public IReadOnlyList<GroupMemberInfo> Members { get; }
    public DocumentSession Session { get; }
    internal ReferenceContext References => _references;
    internal IReadOnlyList<string> OwnedOutputPaths =>
        [.. Members.Select(member => member.RelativePath), .. _declaredOutputPaths];

    public bool Owns(string relativePath) => OwnedOutputPaths.Any(path =>
        string.Equals(path, relativePath, StringComparison.OrdinalIgnoreCase));

    public void CheckFingerprints() => CheckFingerprints(CancellationToken.None);

    public void CheckFingerprints(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var member in Members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = _set.FindById(member.FileId)
                ?? throw new InvalidOperationException($"Owned output '{member.RelativePath}' could not be read. Close and reimport the dump.");
            var actual = OwnedSourceReader.Hash(file, member.RelativePath, cancellationToken);
            if (!string.Equals(actual, member.Sha256, StringComparison.Ordinal))
                throw new InvalidOperationException($"Group input '{member.RelativePath}' changed since selection. Close and reimport the dump.");
        }
        _session?.CheckOutputFingerprints(cancellationToken);
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
        => VerifyOutputs(outputs, finalSet, CancellationToken.None);

    internal void VerifyOutputs(IReadOnlyDictionary<string, ReadOnlyMemory<byte>> outputs, ISaveSet? finalSet, CancellationToken cancellationToken)
    {
        CheckFingerprints(cancellationToken);
        var expected = OwnedOutputPaths;
        if (outputs.Count != expected.Count || outputs.Keys.Any(path => !Owns(path))
            || expected.Any(path => !outputs.ContainsKey(path)))
            throw new SaveFormatException("A document group must serialize every owned output exactly once and cannot write unowned files.");
        var overlay = new OverlaySaveSet(finalSet ?? _set, outputs, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var errors = _groupPlugin.ValidateGroup(Input(overlay), _references);
        cancellationToken.ThrowIfCancellationRequested();
        if (errors.Count > 0) throw new SaveFormatException(string.Join("; ", errors.Select(e => e.Message)));
        foreach (var member in Members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = ((ISaveSet)overlay).FindFile(member.RelativePath)!;
            var reopened = _plugin.Open(file, _references);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                _ = reopened.BuildView();
                cancellationToken.ThrowIfCancellationRequested();
                var completed = reopened;
                reopened = null!;
                completed.Dispose();
            }
            catch (Exception failure)
            {
                SessionCleanup.DisposeAfterFailure(reopened, failure, "Group role verification and cleanup failed.");
                throw;
            }
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

    private IReadOnlyDictionary<string, SemanticMemberContext> ResolveFieldContext(
        StagedGroupDocument staged, IReadOnlyList<string> valueIds)
    {
        var requested = valueIds.Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var members = Members.ToDictionary(member => (member.Role, member.RelativePath));
        var mapped = new Dictionary<string, SemanticMemberContext>(StringComparer.Ordinal);
        long responseBytes = 0;
        foreach (var batch in requested.Chunk(SemanticContextBudget.MaxBatchCount))
        {
            var batchIds = batch.ToHashSet(StringComparer.Ordinal);
            var response = staged.ResolveFieldContext(batch);
            if (response is null)
                throw new SemanticContextResolutionException(mapped, "The group returned more context rows than requested.");
            var responseEntries = 0;
            foreach (var context in response)
            {
                if (++responseEntries > batch.Length)
                    throw new SemanticContextResolutionException(mapped, "The group returned more context rows than requested.");
                if (context is null || string.IsNullOrWhiteSpace(context.ValueId)
                    || !batchIds.Contains(context.ValueId))
                    throw new SemanticContextResolutionException(mapped, "The group returned foreign field context.");
                if (string.IsNullOrWhiteSpace(context.Role) || string.IsNullOrWhiteSpace(context.RelativePath)
                    || context.Role.Length > SemanticContextBudget.MaxStringLength
                    || context.RelativePath.Length > SemanticContextBudget.MaxStringLength
                    || context.Provenance is { Length: > SemanticContextBudget.MaxStringLength })
                    throw new SemanticContextResolutionException(mapped, "The group returned oversized context strings.");
                if (!members.ContainsKey((context.Role, context.RelativePath)))
                    throw new SemanticContextResolutionException(mapped, "The group returned foreign field context.");
                var semantic = new SemanticMemberContext(context.Role, context.RelativePath, Title, context.Provenance);
                if (!SemanticContextBudget.TryEstimate(semantic, out var bytes)
                    || responseBytes > SemanticContextBudget.MaxResponseBytes - bytes)
                    throw new SemanticContextResolutionException(mapped, "The group context response exceeded its bounded byte budget.");
                responseBytes += bytes;
                if (!mapped.TryAdd(context.ValueId, semantic))
                    throw new SemanticContextResolutionException(mapped, "The group returned duplicate field context.");
            }
        }

        return mapped;
    }

    internal static Dictionary<string, ReadOnlyMemory<byte>> FreezeMembers(
        IReadOnlyList<GroupMemberInfo> members, Func<string, ISaveFile?> findById,
        CancellationToken cancellationToken)
    {
        return members.ToDictionary(member => member.RelativePath, member =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = findById(member.FileId)
                ?? throw new InvalidOperationException(
                    $"Owned output '{member.RelativePath}' could not be read. Close and reimport the dump.");
            return (ReadOnlyMemory<byte>)OwnedSourceReader.ReadAllBytes(file, member.RelativePath, cancellationToken);
        }, StringComparer.OrdinalIgnoreCase);
    }
}
