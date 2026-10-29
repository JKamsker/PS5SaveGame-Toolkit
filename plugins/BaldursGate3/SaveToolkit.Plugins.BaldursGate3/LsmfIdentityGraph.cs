using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.BaldursGate3;

internal sealed record LsmfPartyJoin(int PartyRow, int PartyEntityRow, int BodyEntityRow,
    string? TemplateId, int? StateEntityRow, int? StatsRow, int? HealthRow, int? SpellBookRow,
    int? ClassesRow, int? PrimaryInventoryEntityRow, bool IsAmbiguous);

/// <summary>
/// Joins the save-local identity namespaces without using row order or proximity.
/// Party membership comes from MemberComponent, body identity from LSF Creators, and
/// character-state entities from the exact TemplateComponent GUID bridge.
/// </summary>
internal sealed class LsmfIdentityGraph
{
    private const int PartyMemberBytes = 48;
    private const int TemplateBytes = 24;
    private const int OwnerBytes = 24;
    private readonly LsmfAccess _access;
    private readonly IReadOnlyList<LsmfEntity> _entities;
    private readonly IReadOnlyDictionary<string, string> _creators;
    private readonly IReadOnlyDictionary<int, IReadOnlySet<string>> _templates;
    private readonly IReadOnlyDictionary<int, int> _spellCounts;

    internal LsmfIdentityGraph(LsmfAccess access, LarianResource? resource)
    {
        _access = access;
        _entities = access.Model.Entities();
        _creators = ReadCreators(resource);
        _templates = ReadTemplates();
        _spellCounts = ReadSpellCounts();
        Party = ReadParty();
        Diagnostics = BuildDiagnostics();
    }

    internal IReadOnlyList<LsmfPartyJoin> Party { get; }
    internal IReadOnlyList<string> Diagnostics { get; }
    private IReadOnlyList<LsmfPartyJoin> ReadParty()
    {
        var members = _access.Rows("game.party.v0.MemberComponent", PartyMemberBytes);
        var result = new List<LsmfPartyJoin>(members.Count);
        var bytes = _access.Model.Bytes.Span;
        for (var partyRow = 0; partyRow < members.Count; partyRow++)
        {
            var member = members[partyRow];
            if (member.EntityRow is not { } partyEntity) continue;
            var bodyCandidates = new HashSet<int>();
            for (var field = 0; field + sizeof(ulong) <= PartyMemberBytes; field += sizeof(ulong))
            {
                var pointer = BinaryData.U64(bytes, member.Offset + field);
                if (!TryEntity(pointer, out var body) || !_creators.ContainsKey(_entities[body].Guid)) continue;
                bodyCandidates.Add(body);
            }
            if (bodyCandidates.Count == 0 && _creators.ContainsKey(_entities[partyEntity].Guid)) bodyCandidates.Add(partyEntity);
            if (bodyCandidates.Count != 1)
            {
                if (bodyCandidates.Count > 1)
                    result.Add(new LsmfPartyJoin(partyRow, partyEntity, -1, null, null, null, null, null, null, null, true));
                continue;
            }

            var bodyEntity = bodyCandidates.Single();
            var template = _creators.GetValueOrDefault(_entities[bodyEntity].Guid) ??
                (_templates.GetValueOrDefault(bodyEntity)?.Count == 1 ? _templates[bodyEntity].Single() : null);
            var state = StateCandidate(template);
            var ownerRow = _access.RowsForEntity("game.inventory.v0.OwnerComponent", OwnerBytes, bodyEntity).FirstOrDefault(-1);
            int? primary = null;
            if (ownerRow >= 0)
            {
                var pointer = BinaryData.U64(bytes, _access.Column("game.inventory.v0.OwnerComponent", OwnerBytes)!.Offset + ownerRow * OwnerBytes + 16);
                if (TryEntity(pointer, out var inventory)) primary = inventory;
            }
            // Current container rows are owned by the party member entity in this
            // profile; the body entity is a separate Creator identity. Preserve
            // both joins and use the exact party row as the storage root fallback.
            if (primary is null && _access.Column("game.inventory.v1.ContainerComponent", 32) is not null &&
                _access.RowsForEntity("game.inventory.v1.ContainerComponent", 32, partyEntity).Length == 1)
                primary = partyEntity;
            result.Add(new LsmfPartyJoin(partyRow, partyEntity, bodyEntity, template, state,
                PreferredRow("game.stats.v3.StatsComponent", 36, state),
                PreferredRow("game.stats.v0.HealthComponent", 32, state),
                PreferredRow("game.spell.v3.SpellBookComponent", 16, state),
                PreferredRow("game.stats.v0.ClassesComponent", 16, state), primary, false));
        }
        return result;
    }

    private int? StateCandidate(string? template)
    {
        if (template is null) return null;
        var candidates = _templates.Where(pair => pair.Value.Contains(template))
            .Select(pair => pair.Key)
            .Where(entity => ComponentRow("game.stats.v0.ClassesComponent", 16, entity) is not null ||
                ComponentRow("game.stats.v3.StatsComponent", 36, entity) is not null ||
                ComponentRow("game.spell.v3.SpellBookComponent", 16, entity) is not null)
            .Select(entity => new
            {
                Entity = entity,
                Classes = ComponentRow("game.stats.v0.ClassesComponent", 16, entity) is not null,
                Stats = ComponentRow("game.stats.v3.StatsComponent", 36, entity) is not null,
                Spells = BestSpellCount(entity),
            })
            .OrderByDescending(candidate => candidate.Classes ? 1 : 0)
            .ThenByDescending(candidate => candidate.Stats ? 1 : 0)
            .ThenByDescending(candidate => candidate.Spells)
            .ThenBy(candidate => candidate.Entity)
            .ToArray();
        if (candidates.Length == 0) return null;
        var best = candidates[0];
        var ties = candidates.Count(candidate => candidate.Classes == best.Classes && candidate.Stats == best.Stats && candidate.Spells == best.Spells);
        return ties == 1 ? best.Entity : null;
    }

    private IReadOnlyDictionary<int, IReadOnlySet<string>> ReadTemplates()
    {
        var rows = _access.Rows("game.templates.v0.TemplateComponent", TemplateBytes);
        var bytes = _access.Model.Bytes.Span;
        var result = new Dictionary<int, HashSet<string>>();
        foreach (var row in rows)
        {
            if (row.EntityRow is not { } entity) continue;
            var pointer = BinaryData.U64(bytes, row.Offset);
            var length = BinaryData.U32(bytes, row.Offset + 8);
            try
            {
                if (length == 36)
                {
                    if (!result.TryGetValue(entity, out var values)) result[entity] = values = new HashSet<string>(StringComparer.Ordinal);
                    values.Add(_access.String(pointer, length));
                }
            }
            catch (SaveFormatException) { }
        }
        return result.ToDictionary(pair => pair.Key, pair => (IReadOnlySet<string>)pair.Value);
    }

    private IReadOnlyDictionary<int, int> ReadSpellCounts()
    {
        try
        {
            return LsmfSpellBooks.Read(_access).ToDictionary(book => book.Row, book => book.Spells.Count);
        }
        catch (SaveFormatException) { return new Dictionary<int, int>(); }
    }

    private int? ComponentRow(string name, int width, int? entity) => entity is { } value
        ? _access.RowForEntity(name, width, value)
        : null;

    private int? PreferredRow(string name, int width, int? entity)
    {
        if (entity is not { } value) return null;
        var rows = _access.RowsForEntity(name, width, value);
        if (rows.Length <= 1) return rows is [var row] ? row : null;
        if (name == "game.stats.v0.HealthComponent") return rows[0];
        if (name == "game.spell.v3.SpellBookComponent")
        {
            var ranked = rows.Select(row => (Row: row, Count: _spellCounts.GetValueOrDefault(row)))
                .OrderByDescending(item => item.Count).ToArray();
            return ranked.Length > 1 && ranked[0].Count == ranked[1].Count ? null : ranked[0].Row;
        }
        return null;
    }

    private int BestSpellCount(int entity)
    {
        var rows = _access.RowsForEntity("game.spell.v3.SpellBookComponent", 16, entity);
        return rows.Length == 0 ? 0 : rows.Max(row => _spellCounts.GetValueOrDefault(row));
    }

    private bool TryEntity(ulong pointer, out int row)
    {
        try { row = _access.Model.EntityRow(pointer); return true; }
        catch (SaveFormatException) { row = -1; return false; }
    }

    private IReadOnlyDictionary<string, string> ReadCreators(LarianResource? resource)
    {
        if (resource is null) return new Dictionary<string, string>();
        var pairs = resource.Nodes.Where(node => node.Name == "Creator" && node.Parent >= 0 &&
                resource.Nodes[node.Parent].Name == "Creators")
            .Select(node => (Entity: ResourceIdentifiers.Read(resource, node, "Entity", LarianAttributeType.UUID),
                Template: ResourceIdentifiers.Read(resource, node, "TemplateID", LarianAttributeType.UUID) ??
                    ResourceIdentifiers.Read(resource, node, "TemplateID", LarianAttributeType.FixedString)))
            .Where(pair => pair.Entity is not null && pair.Template is not null)
            .GroupBy(pair => pair.Entity!, StringComparer.Ordinal)
            .Where(group => group.Select(pair => pair.Template).Distinct(StringComparer.Ordinal).Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Template!, StringComparer.Ordinal);
        return pairs;
    }

    private IReadOnlyList<string> BuildDiagnostics()
    {
        var diagnostics = new List<string>();
        if (Party.Count == 0) diagnostics.Add("Party MemberComponent is absent or no member has an unambiguous Creator body reference.");
        if (Party.Any(member => member.StateEntityRow is null)) diagnostics.Add("At least one party body has no unique TemplateComponent state entity; dependent rows remain unjoined.");
        if (Party.Any(member => member.PrimaryInventoryEntityRow is null)) diagnostics.Add("At least one party body has no resolvable primary inventory pointer.");
        return diagnostics;
    }


}
