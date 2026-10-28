using System.Globalization;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>Bounded, marker-only inspection of the separate Osiris story payload.</summary>
internal sealed class StoryInspection
{
    internal const string Prefix = "story/";
    private const int MaxStoryBytes = 64 * 1024 * 1024;
    private static readonly string[] KnownDatabases =
    [
        "DB_Players", "DB_Avatars", "DB_PartOfTheTeam", "DB_ApprovalRating",
        "DB_Quests", "Journal", "Waypoint", "Recipe", "Camp",
    ];

    private readonly Lazy<IReadOnlyList<Marker>> _markers;

    internal StoryInspection(IReadOnlyList<DocumentEntry> entries)
    {
        _markers = new(() => Scan(entries));
    }

    internal IReadOnlyList<ViewNode> Build() =>
    [
        new TextNode("StorySave.bin is inspected read-only. This bounded pass reports only database-name markers that are actually present in the expanded payload; it does not decode Osiris tuple values, XOR-obfuscated strings, networks or quest completion semantics.") { Tone = Tone.Info },
        SectionNode.Lazy("Story and approval database markers", Prefix + "databases"),
    ];

    internal IReadOnlyList<ViewNode> Expand(string id)
    {
        if (id != Prefix + "databases")
        {
            throw new KeyNotFoundException("Unknown BG3 story inspection node.");
        }

        var markers = _markers.Value;
        return
        [
            new StatsNode([new Stat("Known marker families", markers.Count.ToString(CultureInfo.InvariantCulture))]),
            new TableNode(
                [new("name", "Database or marker"), new("occurrences", "Byte occurrences"), new("confidence", "Meaning")],
                markers.Select(marker => new TableRow(marker.Name, new Dictionary<string, ViewNode>
                {
                    ["name"] = new TextNode(marker.Name),
                    ["occurrences"] = new TextNode(marker.Occurrences.ToString(CultureInfo.InvariantCulture)),
                    ["confidence"] = new TextNode("Presence only; tuple values and joins unknown"),
                })).ToArray())
            {
                Title = "Present story marker families",
                Description = "A marker is not evidence that a quest, approval value, party membership or waypoint is active. Missing markers are unknown, not false.",
            },
        ];
    }

    private static IReadOnlyList<Marker> Scan(IReadOnlyList<DocumentEntry> entries)
    {
        var story = entries.FirstOrDefault(entry => entry.Name.Equals("StorySave.bin", StringComparison.OrdinalIgnoreCase));
        if (story is null || story.Bytes.Length > MaxStoryBytes)
        {
            return [];
        }

        var bytes = story.Bytes.AsSpan();
        var markers = new List<Marker>();
        foreach (var name in KnownDatabases)
        {
            var occurrences = Count(bytes, Encoding.UTF8.GetBytes(name)) + CountUtf16(bytes, name);
            if (occurrences > 0)
            {
                markers.Add(new Marker(name, occurrences));
            }
        }

        return markers;
    }

    private static int Count(ReadOnlySpan<byte> haystack, ReadOnlySpan<byte> needle)
    {
        var count = 0;
        for (var offset = 0; offset <= haystack.Length - needle.Length; offset++)
        {
            if (!haystack[offset..].StartsWith(needle)) continue;
            count++;
            offset += needle.Length - 1;
        }

        return count;
    }

    private static int CountUtf16(ReadOnlySpan<byte> haystack, string value)
    {
        var needle = Encoding.Unicode.GetBytes(value);
        return Count(haystack, needle);
    }

    private sealed record Marker(string Name, int Occurrences);
}
