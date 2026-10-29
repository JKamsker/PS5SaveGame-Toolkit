using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.SuckerPunch.Tests;

/// <summary>Authored PSF fixture with nonzero reserved bytes, an unknown entry and a trailing sentinel.</summary>
internal static class CaptionTestData
{
    internal const string ActionId = "metadata/set-save-caption";
    internal const string ValueId = "metadata/sce_sys/param.sfo/SUBTITLE";
    internal const string SfoPath = "sce_sys/param.sfo";
    internal const string Root = "PPSA02225/sdimg_CAPTION/";
    internal const string Original = "Moon";
    internal const int DataStart = 80;
    internal const int TailLength = 7;
    internal const int DefaultCapacity = 32;

    internal static byte[] Sfo(string text = Original, int capacity = DefaultCapacity, ushort format = 0x0204, string key = "SUBTITLE")
    {
        var payload = Encoding.UTF8.GetBytes(text + '\0');
        var bytes = Enumerable.Repeat((byte)0xAB, DataStart + capacity + sizeof(uint) + TailLength).ToArray();
        using var writer = new BinaryWriter(new MemoryStream(bytes, writable: true), Encoding.UTF8);
        writer.Write(0x46535000U);
        writer.Write(0x00000101U);
        writer.Write(52);
        writer.Write(DataStart);
        writer.Write(2);
        writer.Write((ushort)0);
        writer.Write(format);
        writer.Write(payload.Length);
        writer.Write(capacity);
        writer.Write(0);
        writer.Write((ushort)(key.Length + 1));
        writer.Write((ushort)0xFFFF);
        writer.Write(sizeof(uint));
        writer.Write(sizeof(uint));
        writer.Write(capacity);
        writer.Write(Encoding.ASCII.GetBytes(key + "\0OPAQUE\0"));
        payload.CopyTo(bytes, DataStart);
        return bytes;
    }

    internal static ISaveFile File(byte[]? sfo, SyntheticSave? save = null, string role = "dis.dat", string title = "PPSA02225")
    {
        var files = new Dictionary<string, byte[]> { [role] = (save ?? new SyntheticSave(false)).Bytes, ["other.bin"] = [0x19, 0x71] };
        if (sfo is not null) files.Add(SfoPath, sfo);
        return new MemorySaveSet().With(title, "CAPTION", files).Directories[0].Files[0];
    }

    internal static Dictionary<string, FieldValue> Parameters(string text) => new() { ["caption"] = FieldValue.Of(text) };
    internal static ActionPlan Plan(ISaveDocument document, string text, ActionScope? scope = null) =>
        ((IActionPlanningDocument)document).PlanAction(ActionId, Parameters(text), scope ?? new());
    internal static EditOutcome Apply(ISaveDocument document, string text) =>
        ((IActionPlanningDocument)document).ApplyActionPlan(ActionId, Parameters(text), new(), Plan(document, text));
    internal static byte[] Metadata(ISaveDocument document) => document.Serialize().Single(output => output.RelativePath.EndsWith(SfoPath, StringComparison.Ordinal)).Content.ToArray();
    internal static byte[] Primary(ISaveDocument document) => document.Serialize().Single(output => output.RelativePath.EndsWith(".dat", StringComparison.Ordinal)).Content.ToArray();
    internal static string Text(byte[] bytes) => ParamSfoDocument.Parse(bytes).PlanExistingUtf8("SUBTITLE", "").OriginalText;

    internal static byte[] Zip(IEnumerable<KeyValuePair<string, byte[]>> files)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (path, bytes) in files)
            {
                using var entry = archive.CreateEntry(path).Open();
                entry.Write(bytes);
            }
        return output.ToArray();
    }
}
