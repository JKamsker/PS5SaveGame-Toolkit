using SaveToolkit.Sdk.References;

namespace SaveToolkit.Sdk.Views;

/// <summary>Checks publication bounds, link safety, provenance and graph structure without I/O.</summary>
public static class SpatialViewValidation
{
    private static readonly string[] PathPrefixes = ["section:", "tab:", "row:"];
    /// <summary>Rejects unbounded or malformed graph metadata, duplicate identities, missing endpoints and cycles.</summary>
    public static void Validate(GraphNode graph)
    {
        Text(graph.Title);
        Text(graph.TextAlternative);
        Require(graph.Items is { Count: <= SpatialViewLimits.MaxItems }, "Too many graph items.");
        Require(graph.Edges is { Count: <= SpatialViewLimits.MaxEdges }, "Too many graph edges.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in graph.Items)
        {
            Require(item is not null, "Missing graph item.");
            Id(item.Id);
            Require(ids.Add(item.Id), "Duplicate graph item id.");
            Text(item.Label);
            Text(item.Description, optional: true);
            Text(item.Cost, optional: true);
            Text(item.Branch, optional: true);
            Require(Enum.IsDefined(item.State), "Unknown graph state.");
            Target(item.Target, allowRow: false);
        }

        var outgoing = ids.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        var incoming = ids.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        var edges = new HashSet<(string, string)>();
        foreach (var edge in graph.Edges)
        {
            Require(edge is not null && ids.Contains(edge.From) && ids.Contains(edge.To), "Unknown graph endpoint.");
            Require(edges.Add((edge.From, edge.To)), "Duplicate graph edge.");
            outgoing[edge.From].Add(edge.To);
            incoming[edge.To]++;
        }

        var queue = new Queue<string>(ids.Where(id => incoming[id] == 0));
        var visited = 0;
        while (queue.TryDequeue(out var id))
        {
            visited++;
            foreach (var next in outgoing[id])
            {
                if (--incoming[next] == 0) queue.Enqueue(next);
            }
        }

        Require(visited == ids.Count, "Graph prerequisites must be acyclic.");
    }

    /// <summary>Rejects unbounded or malformed map metadata, coordinates, provenance assertions and source links.</summary>
    public static void Validate(MapNode map)
    {
        Text(map.Title);
        Text(map.TextAlternative);
        Text(map.CoordinateSpace);
        Require(map.Bounds is not null && map.Transform is not null, "Missing map bounds or transform.");
        var b = map.Bounds;
        var t = map.Transform;
        Require(new[] { b.MinX, b.MinY, b.MaxX, b.MaxY, t.ScaleX, t.ScaleY, t.OffsetX, t.OffsetY }.All(Coordinate),
            "Invalid map bounds or transform.");
        Require(b.MinX < b.MaxX && b.MinY < b.MaxY && t.ScaleX != 0 && t.ScaleY != 0,
            "Map bounds must have area and transforms must have nonzero scales.");
        Link(map.ExternalSource);
        if (map.Background is { } background)
        {
            Text(background.AssetId);
            Require(IsPackAsset(background.AssetId), "Map background must be a local F2 pack asset id.");
            Text(background.Alt);
            Require(background.EmbeddingPermitted, "Background licence must explicitly permit embedding.");
            Attribution(background.Attribution);
        }

        Require(map.Markers is { Count: <= SpatialViewLimits.MaxItems }, "Too many map markers.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var positioned = false;
        foreach (var marker in map.Markers)
        {
            Require(marker is not null, "Missing map marker.");
            Id(marker.Id);
            Require(ids.Add(marker.Id), "Duplicate map marker id.");
            Text(marker.Label);
            Text(marker.Category);
            Text(marker.Description, optional: true);
            Require(Enum.IsDefined(marker.Status), "Unknown marker status.");
            Require(marker.Confidence is null || Enum.IsDefined(marker.Confidence.Value), "Unknown marker confidence.");
            Target(marker.Target, allowRow: true);
            Link(marker.ExternalLink);
            Require(marker.X.HasValue == marker.Y.HasValue, "Supply both marker coordinates or neither.");
            if (marker.X is not { } x || marker.Y is not { } y) continue;
            positioned = true;
            var displayX = x * t.ScaleX + t.OffsetX;
            var displayY = y * t.ScaleY + t.OffsetY;
            Require(Coordinate(x) && Coordinate(y) && Coordinate(displayX) && Coordinate(displayY)
                && displayX >= b.MinX && displayX <= b.MaxX && displayY >= b.MinY && displayY <= b.MaxY,
                "Marker lies outside the map bounds or has invalid coordinates.");
        }

        if (positioned)
        {
            Require(map.CoordinateEmbeddingPermitted, "Coordinate licence must explicitly permit embedding.");
            Attribution(map.CoordinateAttribution);
        }
    }

    private static void Target(ViewTarget? target, bool allowRow)
    {
        if (target is null) return;
        Id(target.Id);
        Require(Enum.IsDefined(target.Kind) && (allowRow || target.Kind != ViewTargetKind.Row), "Invalid navigation target.");
        Require(target.Path is { Count: <= SpatialViewLimits.MaxPathParts }, "Navigation path is too long.");
        foreach (var part in target.Path)
        {
            Text(part);
            Require(PathPrefixes.Any(prefix =>
                part.StartsWith(prefix, StringComparison.Ordinal) && part.Length > prefix.Length), "Invalid navigation path.");
        }
    }

    private static void Link(ExternalViewLink? link)
    {
        if (link is null) return;
        Https(link.Url);
        Text(link.Label);
        Text(link.Attribution);
    }

    private static void Attribution(ReferenceAttribution? attribution)
    {
        Require(attribution is not null, "Embedded map data requires attribution.");
        Https(attribution.SourceUrl);
        Https(attribution.LicenseUrl);
        Text(attribution.Author);
        Text(attribution.License);
        Text(attribution.Modifications);
        Require(attribution.Retrieved != default, "Attribution needs a retrieval date.");
    }

    private static bool IsPackAsset(string id)
    {
        var parts = id.Split(':');
        return parts.Length == 2 && parts[0].Length is > 0 and <= 64
            && parts[0].All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '_' or '-')
            && parts[1].Split('/').All(segment => segment.Length > 0 && segment is not "." and not ".."
                && segment.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'));
    }

    private static void Https(string url)
    {
        Text(url);
        Require(url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && !url.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c == '\\')
            && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.Length > 0 && uri.UserInfo.Length == 0, "External map/graph links must be absolute HTTPS URLs without credentials.");
    }

    private static bool Coordinate(double number) => double.IsFinite(number) && Math.Abs(number) <= SpatialViewLimits.MaxCoordinate;

    private static void Id(string id) => Text(id, limit: SpatialViewLimits.MaxIdLength);

    private static void Text(string? text, bool optional = false, int limit = SpatialViewLimits.MaxTextLength) =>
        Require(optional && text is null || !string.IsNullOrWhiteSpace(text) && text.Length <= limit, "Missing or oversized spatial view text.");

    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
