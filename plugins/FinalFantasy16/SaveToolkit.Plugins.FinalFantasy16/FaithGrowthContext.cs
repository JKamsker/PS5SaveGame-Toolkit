using System.Buffers.Binary;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.FinalFantasy16;

internal sealed class FaithGrowthContext(byte[] bytes, FaithBlock player)
{
    private const int ChildOffset = 8;
    private const int XpOffset = 4;
    private const string Evidence = "Stored level/XP pairs in the supported PlayerData prefix; byte layout checked against PS5 samples. " +
        "No active-stage, growth-curve or experience-threshold join is established. " +
        "Nenkai/010GameTemplates, MIT, revision 4a3685f9b63f72ca49e24ac2f18420aaad2762b8: " +
        "https://github.com/Nenkai/010GameTemplates/blob/4a3685f9b63f72ca49e24ac2f18420aaad2762b8/" +
        "Square%20Enix/Final%20Fantasy%2016/FaithSaveFile_Sub.bt";

    public SectionNode Build(FaithReadField field) => new("Stored growth branches — read-only", [
        new TextNode("Both pairs are displayed independently. A stored level is not an index into a known ParamGrow curve. " +
            "The active adult/child branch, XP basis, normal/FF mode and DLC level cap remain unconfirmed; " +
            "no XP-to-next-level, AP refund or projected stat is calculated."),
        new TableNode([new TableColumn("branch", "Stored branch"), new TableColumn("level", "Level"), new TableColumn("xp", "Experience")],
            new[] { Row(field, "adult", "Default/adult candidate", 0), Row(field, "child", "Child candidate", ChildOffset) }),
    ]);

    private TableRow Row(FaithReadField field, string key, string name, int offset) => new($"growth/{key}",
        new Dictionary<string, ViewNode>
        {
            ["branch"] = new TextNode(name),
            ["level"] = Number(field, $"growth/{key}/level", "Stored level", player.PayloadOffset + offset),
            ["xp"] = Number(field, $"growth/{key}/xp", "Stored experience", player.PayloadOffset + offset + XpOffset),
        });

    private FieldNode Number(FaithReadField field, string id, string label, int offset) => field(id, label,
        IntegerSpec.Int32, () => FieldValue.Of(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset))), Confidence.Verified, Evidence);
}
