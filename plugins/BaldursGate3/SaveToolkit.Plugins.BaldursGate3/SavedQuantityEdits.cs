using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>Only direct, unambiguous integer attributes; never a guessed NewAge address or owner join.</summary>
internal sealed class SavedQuantityEdits
{
    internal const string Warning = "Not validated on console. This is an explicit LSF quantity candidate, not proven live inventory or player gold. " +
        "Limits use the observed positive range for this stored type. Other game integrity/dependency checks are unknown; the game may reject the edited save.";
    private readonly Dictionary<string, Candidate> _byId;
    private readonly Dictionary<(int Entry, int Node), Candidate> _byNode;

    internal SavedQuantityEdits(IReadOnlyList<DocumentEntry> entries)
    {
        var candidates = new List<Candidate>();
        for (var entry = 0; entry < entries.Count; entry++)
        {
            if (!entries[entry].Name.Equals("Globals.lsf", StringComparison.OrdinalIgnoreCase) || entries[entry].Resource is not { } resource) continue;
            for (var node = 0; node < resource.Nodes.Count; node++)
            {
                var current = resource.Nodes[node];
                if (current.Name != "Item" || current.Parent < 0 || resource.Nodes[current.Parent].Name != "Items") continue;
                var matches = current.Attributes.Where(index => IsQuantityName(resource.Attributes[index].Name)).Take(2).ToArray();
                if (matches is not [var index]) continue;
                var attribute = resource.Attributes[index];
                if (!AttributeEncoder.CanWrite(attribute) || AttributeDecoder.Read(attribute) is not { Kind: FieldValueKind.Integer } value ||
                    value.AsInteger() <= 0) continue;
                candidates.Add(new Candidate(entry, node, index, attribute, value.AsInteger()));
            }
        }

        // A shared attribute cannot safely stand for two separate item occurrences.
        candidates = candidates.GroupBy(candidate => candidate.Id).Where(group => group.Count() == 1).Select(group => group.Single()).ToList();
        foreach (var group in candidates.GroupBy(candidate => candidate.Attribute.Type))
        {
            var max = group.Max(candidate => candidate.Original);
            foreach (var candidate in group) candidate.Max = max;
        }
        _byId = candidates.ToDictionary(candidate => candidate.Id, StringComparer.Ordinal);
        _byNode = candidates.ToDictionary(candidate => (candidate.Entry, candidate.Node));
    }

    internal Candidate? Find(string id) => _byId.GetValueOrDefault(id);
    internal Candidate? Find(int entry, int node) => _byNode.GetValueOrDefault((entry, node));

    private static bool IsQuantityName(string name) => name is "Quantity" or "Amount" or "Count" or "StackAmount" or "StackCount";

    internal sealed class Candidate(int entry, int node, int attributeIndex, ResourceAttribute attribute, Int128 original)
    {
        internal int Entry { get; } = entry;
        internal int Node { get; } = node;
        internal ResourceAttribute Attribute { get; } = attribute;
        internal Int128 Original { get; } = original;
        internal Int128 Max { get; set; }
        internal string Id => $"resource/{Entry}/attribute/{attributeIndex}";
        internal IntegerSpec Spec => new(1, Max);
    }
}
