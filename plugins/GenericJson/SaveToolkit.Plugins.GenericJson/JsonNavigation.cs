using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.GenericJson;

internal static class JsonNavigation
{
    public static ViewTarget Field(JsonValueToken node)
    {
        var ancestors = new Stack<JsonValueToken>();
        for (var parent = node.Parent; parent is not null; parent = parent.Parent) ancestors.Push(parent);
        var path = new List<string> { "tab:JSON" };
        var chain = ancestors.ToArray();
        for (var i = 0; i < chain.Length; i++)
        {
            var parent = chain[i];
            var child = i + 1 < chain.Length ? chain[i + 1] : node;
            path.Add("section:" + parent.Label);
            var start = 0;
            var count = parent.Children.Count;
            while (true)
            {
                path.Add($"section:children/{parent.Index}/{start}/{count}");
                if (count <= JsonSaveDocument.PageSize) break;
                var chunk = Math.Max(JsonSaveDocument.PageSize, (count + JsonSaveDocument.PageSize - 1) / JsonSaveDocument.PageSize);
                var end = start + count;
                start += (child.Position - start) / chunk * chunk;
                count = Math.Min(chunk, end - start);
            }
        }
        return new(ViewTargetKind.Field, node.ValueId) { Path = path };
    }
}
