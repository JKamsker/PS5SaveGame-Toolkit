using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.References;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Sessions.Groups;

public sealed record DocumentGroupOffer(string Token, string PluginId, string Title,
    IReadOnlyList<GroupMemberInfo> Members, IReadOnlyList<FieldError> Errors);

/// <summary>Workspace-scoped ownership and bounded, fingerprint-bound confirmation offers.</summary>
internal sealed class DocumentGroupRegistry(Workspace workspace, PluginCatalog catalog, ReferencePackStore? packs, Lock gate)
{
    private const int MaxOffers = 128;
    private const int MaxMembers = 32;
    private readonly Dictionary<string, DocumentGroupOffer> _offers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DocumentGroupSession> _groups = new(StringComparer.Ordinal);

    public IReadOnlyList<DocumentGroupSession> All { get { lock (gate) return [.. _groups.Values]; } }

    public IReadOnlyList<DocumentGroupOffer> Propose(string fileId, string pluginId)
    {
        lock (gate)
        {
            var anchor = workspace.Set.FindById(fileId) ?? throw new KeyNotFoundException("Unknown group anchor.");
            var plugin = catalog.Find(pluginId) ?? throw new KeyNotFoundException("Unknown group plugin.");
            if (plugin is not IDocumentGroupPlugin groupPlugin) return [];
            var proposals = groupPlugin.ProposeGroups(anchor);
            if (proposals.Count > MaxMembers) throw new SaveFormatException("Too many group alternatives; narrow the selection.");
            var offers = new List<DocumentGroupOffer>();
            foreach (var proposal in proposals)
            {
                var errors = new List<FieldError>(proposal.Errors);
                var members = new List<GroupMemberInfo>();
                if (proposal.Members.Count is < 2 or > MaxMembers)
                    errors.Add(new("", $"A group requires 2–{MaxMembers} explicit members."));
                else
                {
                    var roles = new HashSet<string>(StringComparer.Ordinal);
                    var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var member in proposal.Members)
                    {
                        if (string.IsNullOrWhiteSpace(member.Role) || !roles.Add(member.Role) || !paths.Add(member.RelativePath))
                            errors.Add(new(member.Role, "Group roles and paths must be nonempty and unique."));
                        if (((ISaveSet)workspace.Set).FindFile(member.RelativePath) is not DiskSaveFile file || file.IsSystemFile)
                        {
                            errors.Add(new(member.Role, $"Missing game file '{member.RelativePath}'."));
                            continue;
                        }
                        if (file.Directory.Generation != anchor.Directory.Generation
                            || !string.Equals(file.Directory.TitleId, anchor.Directory.TitleId, StringComparison.OrdinalIgnoreCase))
                            errors.Add(new(member.Role, "Every group member must have the anchor's title and imported generation."));
                        members.Add(new(member.Role, file.Id, file.RelativePath, file.Sha256, file.Directory.IsBackup));
                    }
                    if (!members.Any(m => m.FileId == fileId)) errors.Add(new("", "A group must contain its selected anchor."));
                }
                if (errors.Count == 0)
                {
                    var input = new DocumentGroupInput(Array.AsReadOnly(members.Select(m =>
                        new DocumentGroupFile(m.Role, (ISaveFile)workspace.Set.FindById(m.FileId)!)).ToArray()));
                    errors.AddRange(groupPlugin.ValidateGroup(input, Pin(plugin, anchor)));
                }
                var offer = new DocumentGroupOffer(Guid.NewGuid().ToString("N"), pluginId, proposal.Title,
                    members.AsReadOnly(), errors.AsReadOnly());
                while (_offers.Count >= MaxOffers) _offers.Remove(_offers.Keys.First());
                _offers.Add(offer.Token, offer);
                offers.Add(offer);
            }
            return offers;
        }
    }

    public DocumentGroupSession Open(string token, string id, string? releaseCleanDocumentId, DocumentSessionOptions? options,
        out DocumentViewSnapshot view)
    {
        lock (gate)
        {
            if (!_offers.TryGetValue(token, out var offer)) throw new InvalidOperationException("The group proposal expired. Select its members again.");
            if (offer.Errors.Count > 0) throw new SaveFormatException(string.Join("; ", offer.Errors.Select(e => e.Message)));
            if (releaseCleanDocumentId is not null && (workspace.FindDocument(releaseCleanDocumentId) is not { } release
                || !offer.Members.Any(m => m.FileId == release.File.Id)))
                throw new InvalidOperationException("Only a selected group's clean member session may be released.");
            foreach (var member in offer.Members)
            {
                ThrowIfOwned(member.RelativePath);
                foreach (var document in workspace.Documents)
                {
                    if (document.Id == releaseCleanDocumentId && !document.State.IsDirty) continue;
                    // Never replace an existing session, even a clean one: another request may hold
                    // its reference. Explicit close/disposal prevents an in-flight writer surviving.
                    if (document.File.Id == member.FileId || document.State.IsDirty && document.Serialize().Any(o =>
                        string.Equals(o.RelativePath, member.RelativePath, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException($"'{member.RelativePath}' is open in {(document.State.IsDirty ? "a dirty" : "another")} session ({document.Id}). Close it before opening this group.");
                }
            }
            var plugin = catalog.Find(offer.PluginId)!;
            var first = workspace.Set.FindById(offer.Members[0].FileId)!;
            var group = new DocumentGroupSession(id, offer.Title, workspace.Set, plugin, offer.Members, Pin(plugin, first), options);
            try
            {
                view = group.Session.CaptureView();
                if (releaseCleanDocumentId is not null && !workspace.ReleaseCleanDocument(releaseCleanDocumentId))
                    throw new InvalidOperationException("The member acquired unsaved edits while opening the group. No group was opened.");
                _groups.Add(id, group);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                SessionOpening.DisposeFailed(group.Session, failure);
                throw;
            }
            _offers.Remove(token);
            return group;
        }
    }

    public void ThrowIfOwned(string path)
    {
        lock (gate)
            if (_groups.Values.FirstOrDefault(g => g.Owns(path)) is { } owner)
                throw new InvalidOperationException($"'{path}' is owned by document group '{owner.Session.Id}'. Close that group first.");
    }

    public void Remove(string id) { lock (gate) _groups.Remove(id); }

    private ReferenceContext Pin(IGamePlugin plugin, DiskSaveFile anchor) =>
        packs?.Pin(plugin, anchor.Directory.TitleId) ?? ReferenceContext.None;
}
