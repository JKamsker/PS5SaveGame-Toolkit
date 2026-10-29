using System.Text.Json;
using SaveToolkit.Sdk.Diff;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Plugins.GenericJson;

/// <summary>Explicit evidence hook: matched gameplay/settings only, never the whole private profile.</summary>
internal sealed class JsonTitleDecoder : ISaveDecoderHook
{
    public string Id => "genericjson.title-values-v1";

    public bool CanDecode(ISaveFile file)
    {
        if (file.Length is <= 0 or > JsonTokenTree.MaxBytes) return false;
        if (file.Directory.TitleId is not ("PPSA02343" or "PPSA04027" or "PPSA07696")) return false;
        if (file.FileName is not ("ue4savegame.dpx.sav" or "settings.json" or "imgui_emu_option.json")) return false;
        try { return JsonOverlay.Match(file, JsonTokenTree.Parse(file.ReadAllBytes())) is not null; }
        catch (SaveToolkit.Sdk.SaveFormatException) { return false; }
    }

    public void Decode(ISaveFile file, ISaveDecodeSink sink)
    {
        if (file.Length > sink.MaxFileBytes || file.Length > JsonTokenTree.MaxBytes)
        {
            sink.MarkTruncated("JSON exceeds the decoder file-byte limit.");
            return;
        }
        var tree = JsonTokenTree.Parse(file.ReadAllBytes());
        var overlay = JsonOverlay.Match(file, tree) ?? throw new InvalidDataException("No matched JSON title/role/schema for this decoder.");
        sink.Warn("Only matched title gameplay/settings are decoded; private profile identity and unrelated JSON are excluded. Archive order is not chronology.");
        if (overlay.IsProfile)
        {
            foreach (var node in overlay.Groups)
                if (!Emit(node, "Profile/" + Segment(node), 1, sink)) return;
        }
        else if (file.Directory.TitleId == "PPSA04027")
            DecodeCyberpunk(overlay, sink);
        else
            foreach (var group in overlay.Groups)
                if (!Emit(group, Segment(group), 0, sink)) return;
    }

    private static void DecodeCyberpunk(JsonOverlay overlay, ISaveDecodeSink sink)
    {
        var groups = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var group in overlay.Groups)
        {
            var name = JsonOverlay.Unique(group, "group_name")!.Current.ToString();
            groups.TryGetValue(name, out var occurrence);
            groups[name] = ++occurrence;
            var options = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var option in JsonOverlay.Unique(group, "options")!.Children)
            {
                if (!JsonOverlay.HasType(JsonOverlay.Unique(option, "name"), JsonScalar.Text)) continue;
                var key = JsonOverlay.Unique(option, "name")!.Current.ToString();
                options.TryGetValue(key, out var count);
                options[key] = ++count;
                var path = $"settings/{Escape(name)}#{occurrence}/{Escape(key)}#{count}";
                foreach (var member in option.Children.Where(n => n.Label is "value" or "index" or "default_value" or "default_index"))
                    if (!Emit(member, path + "/" + Segment(member), 3, sink)) return;
            }
        }
    }

    private static bool Emit(JsonValueToken node, string path, int depth, ISaveDecodeSink sink)
    {
        if (depth > sink.MaxDepth)
        {
            sink.MarkTruncated("JSON title traversal reached the decoder depth limit.");
            return true;
        }
        if (!node.IsContainer)
        {
            var value = node.Current.IsNull ? "null" : node.Current.ToString();
            return sink.Add(path, node.JsonType, value);
        }
        if (node.Kind == JsonTokenType.StartArray)
            sink.MarkTruncated("Array evidence uses positional paths; native identity across reordering is unproven.");
        foreach (var child in node.Children)
            if (!Emit(child, path + "/" + Segment(child), depth + 1, sink)) return false;
        return true;
    }

    private static string Segment(JsonValueToken node) => node.Parent?.Kind == JsonTokenType.StartArray
        ? node.Position.ToString(System.Globalization.CultureInfo.InvariantCulture) : Escape(node.Label) + "#" + node.Occurrence;

    private static string Escape(string text) => text.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
}
