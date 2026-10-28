using System.Security.Cryptography;
using System.Text;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.Cyberpunk2077;

/// <summary>Spatial navigation targets independent read-only details, never unloaded F8 rows.</summary>
internal static class SpatialDetails
{
    public const int PageSize = 128;
    private const string TruncatedNotice = "… See linked detail for full provenance.";

    // RED names can each occupy 254 bytes. Hash the complete identity to keep combined IDs bounded.
    public static string PerkId(InspectionRow row) => "perk/" + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(row.Values["kind"] + "\0" + row.Values["group"] + "\0" + row.Values["name"])));

    public static string Summary(string text) => text.Length <= SpatialViewLimits.MaxTextLength ? text
        : text[..(SpatialViewLimits.MaxTextLength - TruncatedNotice.Length)] + TruncatedNotice;

    public static FieldNode Field(string id, InspectionRow row,
        Func<string, string, FieldSpec, string, FieldNode> readField) =>
        readField(id, row.Values["name"], new TextSpec { Multiline = true, ReadOnly = true },
            string.Join("\n", row.Values.Select(pair => $"{pair.Key}: {pair.Value}"))) with
        { Description = row.Attribution, Confidence = row.Confidence };
}
