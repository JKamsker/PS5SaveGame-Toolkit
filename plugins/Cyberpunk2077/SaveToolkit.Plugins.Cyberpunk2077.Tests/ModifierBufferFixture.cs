using System.Text;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

/// <summary>Independent wire fixtures for the three supported modifier records; no production writer.</summary>
internal static class ModifierBufferFixture
{
    public static byte[] Constant(float value = -3.5F) => Record("gameConstantStatModifier", writer => writer.Write(value));

    public static byte[] Combined(float value = 1.25F) => Record("gameCombinedStatModifier", writer =>
    {
        writer.Write(0x8877665544332211UL); // Reference stat.
        writer.Write(3U); // Combination.
        writer.Write(4U); // Relation.
        writer.Write(value);
    });

    public static byte[] Curve() => Record("gameCurveStatModifier", writer =>
    {
        WriteString(writer, "movement");
        WriteString(writer, "sprint");
        writer.Write(0xFFEEDDCCBBAA0099UL); // Curve stat.
        writer.Write(17U); // Opaque word.
    });

    public static byte[] Buffer(params byte[][] records)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        writer.Write(records.Sum(record => record.Length));
        foreach (var record in records) writer.Write(record);
        return output.ToArray();
    }

    private static byte[] Record(string type, Action<BinaryWriter> body)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, true);
        WriteString(writer, type);
        writer.Write(0x1122334455667788UL); // Stat.
        writer.Write(2U); // Operation.
        body(writer);
        return output.ToArray();
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        CsavCorruptionFixture.WritePacked(writer, -bytes.Length);
        writer.Write(bytes);
    }
}
