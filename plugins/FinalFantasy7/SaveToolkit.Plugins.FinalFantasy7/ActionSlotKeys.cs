using System.Globalization;

namespace SaveToolkit.Plugins.FinalFantasy7;

/// <summary>Validates canonical decimal slot identities without splitting or reformatting them.</summary>
internal static class ActionSlotKeys
{
    public const string InventoryPrefix = "inventory/";
    public const string MateriaPrefix = "materia/";

    public static bool TryParse(string key, string prefix, int count, out int slot)
    {
        slot = 0;
        if (!key.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var number = key.AsSpan(prefix.Length);
        foreach (var character in number)
            if (character is < '0' or > '9') return false;
        return !number.IsEmpty && (number.Length == 1 || number[0] != '0') &&
            int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out slot) && slot >= 0 && slot < count;
    }
}
