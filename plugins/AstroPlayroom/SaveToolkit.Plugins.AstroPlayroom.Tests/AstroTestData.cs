using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

internal static class AstroTestData
{
    public const int AllocationSize = 1024 * 1024;
    public const int TextOffset = 0x400;
    public const string CoinsPath = "PreInstall/Common/NumCoin";
    public const string CountPath = "PreInstall/Common/PlayCount";
    public const string MinimalRoot = "\r\n<PreInstall><Common><PlayCount>1</PlayCount><NumCoin>0</NumCoin></Common></PreInstall>";

    public static byte[] Bytes(string text = MinimalRoot)
    {
        var result = new byte[AllocationSize];
        Encoding.UTF8.GetBytes(text).CopyTo(result, TextOffset);
        return result;
    }

    public static ISaveFile File(byte[] bytes) => MemorySaveSet.SingleFile("memory.dat", bytes, "PPSA01325", "sce_sdmemory");
    public static ISaveDocument Open(byte[] bytes) => new AstroPlayroomPlugin().Open(File(bytes));
    public static byte[] Output(ISaveDocument document) => Assert.Single(document.Serialize()).Content.ToArray();

    public static IReadOnlyList<FieldNode> Fields(ISaveDocument document)
    {
        var fields = new Dictionary<string, FieldNode>(StringComparer.Ordinal);
        var queue = new Queue<ViewNode>();
        queue.Enqueue(document.BuildView());
        var budget = 10000;
        while (queue.TryDequeue(out var node))
        {
            Assert.True(budget-- > 0, "View did not terminate.");
            switch (node)
            {
                case FieldNode field:
                    fields[field.ValueId] = field;
                    break;
                case SectionNode { LazyId: { } id }:
                    var expanded = document.ExpandNode(id);
                    Assert.True(expanded.Count <= 65, "Raw tree exceeded its page bound.");
                    foreach (var child in expanded)
                    {
                        queue.Enqueue(child);
                    }

                    break;
                case SectionNode section:
                    foreach (var child in section.Children)
                    {
                        queue.Enqueue(child);
                    }

                    break;
                case TabsNode tabs:
                    foreach (var child in tabs.Tabs.SelectMany(t => t.Children))
                    {
                        queue.Enqueue(child);
                    }

                    break;
                case TableNode table:
                    foreach (var child in table.Rows.SelectMany(r => r.Cells.Values))
                    {
                        queue.Enqueue(child);
                    }

                    break;
            }
        }

        return fields.Values.ToArray();
    }

    public static FieldNode Find(ISaveDocument document, string path) =>
        Assert.Single(Fields(document), f => f.Description == path);

    public static IReadOnlyList<ISaveFile> Samples() => SampleSaves.Load("PPSA01325").AllDiskFiles
        .Where(f => f.PathInDirectory == "memory.dat").Cast<ISaveFile>()
        .OrderBy(f => f.Directory.Generation).ThenBy(f => f.Directory.IsBackup).ToArray();
}
