using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Gearbox;

/// <summary>Finite iterators used by cold targeted metadata; full views keep their existing paths.</summary>
internal static class GearboxMetadataLookup
{
    internal static bool TryChildAt(ProtoMessage root, string name, int index, out ProtoMessage child)
        => TryChildAt(root, name, index, out child, out _);

    internal static bool TryChildAt(ProtoMessage root, string name, int index, out ProtoMessage child, out bool exhausted)
    {
        child = null!;
        exhausted = false;
        if (index < 0) return false;
        var records = 0;
        foreach (var field in root.Fields)
        {
            if (++records > GearboxInventory.DirectLookupWorkLimit)
            {
                exhausted = true;
                return false;
            }
            if (field.Spec?.Name != name || field.Message is null) continue;
            if (index-- == 0)
            {
                child = field.Message;
                return true;
            }
        }
        return false;
    }

    internal static ProtoMessage RequireChild(ProtoMessage root, string name, int index) =>
        TryChildAt(root, name, index, out var child)
            ? child : throw new SaveFormatException("The requested Gearbox record is no longer present.");

    internal static ProtoMessage RequireChild(ProtoMessage root, string name, int index, bool bounded) =>
        bounded ? RequireChild(root, name, index) :
        root.All(name).ElementAtOrDefault(index)?.Message ??
        throw new SaveFormatException("The requested Gearbox record is no longer present.");

    internal static bool TryChildrenWhere(ProtoMessage root, string name, Func<ProtoMessage, bool> predicate,
        out List<(int Index, ProtoMessage Message)> matches, out bool exhausted)
    {
        matches = [];
        exhausted = false;
        var visited = 0;
        var index = 0;
        foreach (var field in root.Fields)
        {
            if (++visited > GearboxInventory.DirectLookupWorkLimit)
            {
                exhausted = true;
                return false;
            }
            if (field.Spec?.Name != name || field.Message is null) continue;
            if (predicate(field.Message)) matches.Add((index, field.Message));
            index++;
        }
        return true;
    }

    internal static bool HasField(ProtoMessage root, string name, out bool exhausted)
    {
        exhausted = false;
        var visited = 0;
        foreach (var field in root.Fields)
        {
            if (++visited > GearboxInventory.DirectLookupWorkLimit)
            {
                exhausted = true;
                return false;
            }
            if (field.Spec?.Name == name) return true;
        }
        return false;
    }

    internal static bool TryCount(ProtoMessage root, string name, out int count)
    {
        count = 0;
        var visited = 0;
        foreach (var field in root.Fields)
        {
            if (++visited > GearboxInventory.DirectLookupWorkLimit) return false;
            if (field.Spec?.Name == name && ++count > GearboxInventory.DirectLookupWorkLimit) return false;
        }
        return true;
    }

    internal static bool TryFindMessage(ProtoMessage root, string name, Func<ProtoMessage, bool> predicate,
        out ProtoMessage message)
    {
        message = null!;
        var visited = 0;
        foreach (var field in root.Fields)
        {
            if (++visited > GearboxInventory.DirectLookupWorkLimit) return false;
            if (field.Spec?.Name == name && field.Message is { } candidate && predicate(candidate))
            {
                message = candidate;
                return true;
            }
        }
        return false;
    }

    internal static bool TryCanonicalIndex(string token, out int index)
    {
        index = -1;
        if (token.Length == 0 || token.Length > 10 || token[0] == '0' && token.Length > 1) return false;
        if (!int.TryParse(token, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out index) || index < 0) return false;
        return string.Equals(token, index.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
