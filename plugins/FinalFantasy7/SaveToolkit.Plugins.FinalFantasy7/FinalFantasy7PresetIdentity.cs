using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SaveToolkit.Sdk;
using SdkPresetResolution = SaveToolkit.Sdk.PresetResolution;

namespace SaveToolkit.Plugins.FinalFantasy7;

internal static class FinalFantasy7PresetIdentity
{
    public static PresetInstanceResolutionStatus Status(PresetTargetStatus status) => status switch
    {
        PresetTargetStatus.Matched => PresetInstanceResolutionStatus.Resolved,
        PresetTargetStatus.Missing => PresetInstanceResolutionStatus.Missing,
        PresetTargetStatus.Ambiguous => PresetInstanceResolutionStatus.Ambiguous,
        _ => PresetInstanceResolutionStatus.Unsupported,
    };

    public static PresetTarget ParseTarget(PresetInstanceReference reference) =>
        TryTarget(reference, out var target) ? target : throw new ArgumentException("Foreign FFVII semantic identity.");

    public static bool TryTarget(PresetInstanceReference reference, out PresetTarget target)
    {
        target = default!;
        var parts = reference.StableId.Split('/');
        if (reference.Kind is "ff7.item-type" or "ff7.item-type.all-matches" or "ff7.materia-type.all-copies" or "ff7.character-index")
        {
            if (parts.Length != 2 || !uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return false;
            if (reference.Kind is "ff7.item-type" or "ff7.item-type.all-matches")
            {
                if (parts[0] != "ff7:item-type") return false;
                target = new(PresetIdentityDomain.ItemType, id, AllMatches: reference.Kind.EndsWith("all-matches", StringComparison.Ordinal));
            }
            else if (reference.Kind == "ff7.materia-type.all-copies")
            {
                if (parts[0] != "ff7:materia-type") return false;
                target = new(PresetIdentityDomain.MateriaType, id, AllMatches: true);
            }
            else
            {
                if (id >= Ff7Schema.CharacterCount || parts[0] != "ff7:character-index") return false;
                target = new(PresetIdentityDomain.CharacterIndex, id);
            }
        }
        else if (reference.Kind == "ff7.materia-instance")
        {
            if (parts.Length != 3 || parts[0] != "ff7:materia-instance" ||
                !uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var instance) ||
                !uint.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var type)) return false;
            target = new(PresetIdentityDomain.MateriaInstance, instance, type);
        }
        else return false;
        return reference.StableId.StartsWith("ff7:", StringComparison.Ordinal);
    }

    public static PresetInstanceReference ToReference(PresetTarget target, ReferenceData references) => target.Domain switch
    {
        PresetIdentityDomain.ItemType => new(target.AllMatches ? "ff7.item-type.all-matches" : "ff7.item-type", $"ff7:item-type/{target.Id}",
            references.Item(target.Id), "inventory-item-type", Provenance: references.Source("ff7r-items", target.Id)),
        PresetIdentityDomain.MateriaType => new("ff7.materia-type.all-copies", $"ff7:materia-type/{target.Id}",
            references.Materia(target.Id), "materia-type", Provenance: references.Source("ff7r-materia", target.Id)),
        PresetIdentityDomain.MateriaInstance => new("ff7.materia-instance", $"ff7:materia-instance/{target.Id}/{target.TypeId}",
            references.Materia(target.TypeId!.Value), "owned-materia-instance", Provenance: references.Source("ff7r-materia", target.TypeId.Value)),
        PresetIdentityDomain.CharacterIndex => new("ff7.character-index", $"ff7:character-index/{target.Id}",
            references.Character((int)target.Id), "character-index", Provenance: references.CharacterSource((int)target.Id)),
        _ => throw new ArgumentException("Foreign FFVII semantic identity."),
    };

    public static bool HasStableMetadata(PresetInstanceReference requested, PresetTarget target, ReferenceData references)
    {
        var expected = ToReference(target, references);
        return string.Equals(requested.Kind, expected.Kind, StringComparison.Ordinal)
            && string.Equals(requested.StableId, expected.StableId, StringComparison.Ordinal)
            && string.Equals(requested.Label, expected.Label, StringComparison.Ordinal)
            && string.Equals(requested.Role, expected.Role, StringComparison.Ordinal)
            && requested.Path is null && requested.Build is null
            && string.Equals(requested.Provenance, expected.Provenance, StringComparison.Ordinal);
    }

    public static PresetInstanceReference ToCandidate(PresetTarget target, PresetCandidate candidate, ReferenceData references) =>
        target.Domain == PresetIdentityDomain.ItemType
            ? new("ff7.inventory-candidate", $"ff7:inventory-candidate/{target.Id}/{candidate.Slot}",
                references.Item(target.Id), "inventory-stack", candidate.RowKey, Provenance: candidate.Source)
            : target.Domain == PresetIdentityDomain.CharacterIndex
                ? new("ff7.character-index", $"ff7:character-index/{target.Id}",
                    references.Character((int)target.Id), "character-index", candidate.RowKey, Provenance: candidate.Source)
                : new("ff7.materia-candidate", $"ff7:materia-candidate/{candidate.InstanceId ?? 0}/{candidate.TypeId}/{candidate.Slot ?? -1}",
                    references.Materia(candidate.TypeId), "materia-detail", string.IsNullOrEmpty(candidate.RowKey) ? null : candidate.RowKey,
                    Provenance: candidate.Source);

    public static PresetInstanceReference ToAllCopiesCandidate(PresetTarget target,
        IReadOnlyList<PresetCandidate> candidates, ReferenceData references)
    {
        var signature = string.Join("\n", candidates.OrderBy(candidate => candidate.RowKey, StringComparer.Ordinal)
            .Select(candidate => $"{candidate.RowKey}\0{candidate.InstanceId}\0{candidate.TypeId}\0{candidate.DetailStatus}"));
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)));
        return new("ff7.materia-all-copies", $"ff7:materia-type/{target.Id}/all-copies/{fingerprint}",
            references.Materia(target.Id), "materia-all-copies", Provenance: references.Source("ff7r-materia", target.Id));
    }

    public static string CandidateRow(PresetTarget target, PresetInstanceReference candidate)
    {
        var parts = candidate.StableId.Split('/');
        return target.Domain switch
        {
            PresetIdentityDomain.ItemType => $"inventory/{parts[^1]}",
            PresetIdentityDomain.CharacterIndex => $"character/{parts[^1]}",
            PresetIdentityDomain.MateriaInstance or PresetIdentityDomain.MateriaType => $"materia/{parts[^1]}",
            _ => throw new ArgumentException("Foreign FFVII semantic identity."),
        };
    }

    public static bool Equivalent(SdkPresetResolution left, SdkPresetResolution right) =>
        left.Instances.Count == right.Instances.Count && left.Instances.Zip(right.Instances).All(pair =>
            pair.First.Requested == pair.Second.Requested && pair.First.Status == pair.Second.Status &&
            pair.First.Message == pair.Second.Message && pair.First.Candidates.Select(item => item.StableId)
                .SequenceEqual(pair.Second.Candidates.Select(item => item.StableId), StringComparer.Ordinal));
}
