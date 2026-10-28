using System.Text.Json;
using System.Text.RegularExpressions;
using SaveToolkit.Core;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.Storage;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using Xunit;

namespace SaveToolkit.Testing;

/// <summary>
/// Contract every plugin must satisfy. Derive one test class per plugin:
/// <code>
/// public sealed class PragmataConformance : PluginConformanceTests&lt;PragmataPlugin&gt;
/// {
///     protected override IReadOnlyList&lt;string&gt; SampleTitleIds =&gt; ["PPSA02530"];
/// }
/// </code>
/// Override the virtual members to tune sample selection or to supply a meaningful edit.
/// </summary>
public abstract partial class PluginConformanceTests<TPlugin>
    where TPlugin : IGamePlugin, new()
{
    protected TPlugin Plugin { get; } = new();

    /// <summary>Title ids whose sample zips contain files this plugin must open.</summary>
    protected abstract IReadOnlyList<string> SampleTitleIds { get; }

    /// <summary>Caps how many distinct files per title are opened (big titles are slow).</summary>
    protected virtual int MaxFilesPerTitle => 6;

    /// <summary>Maximum number of lazy sections expanded while walking a view.</summary>
    protected virtual int LazyExpansionBudget => 150;

    protected virtual bool IsWritable => Plugin.Info.Maturity != PluginMaturity.ReadOnly;

    /// <summary>Files the plugin should handle. Default: probed matches, primary copies first, distinct content.</summary>
    protected virtual IEnumerable<ISaveFile> SelectFiles(DiskSaveSet set) => set.AllDiskFiles
        .Where(f => !f.IsSystemFile && Plugin.Probe(f).Score > ProbeScore.Fallback)
        .OrderBy(f => f.Directory.Generation)
        .ThenBy(f => f.Directory.IsBackup)
        .DistinctBy(f => f.Sha256)
        .Take(MaxFilesPerTitle);

    /// <summary>
    /// The edit used by the round-trip test. Default: first writable integer/bool/float field nudged by one.
    /// Return an empty list to skip the edit test for a particular file.
    /// </summary>
    protected virtual IReadOnlyList<ValueEdit> ChooseEdits(ISaveDocument document, IReadOnlyList<FieldNode> fields)
    {
        foreach (var field in fields)
        {
            if (field.Spec.ReadOnly)
            {
                continue;
            }

            FieldValue? next = field.Spec switch
            {
                BoolSpec => FieldValue.Of(!field.Value.AsBool()),
                IntegerSpec s => field.Value.AsInteger() < s.Max ? FieldValue.Of(field.Value.AsInteger() + 1) : FieldValue.Of(field.Value.AsInteger() - 1),
                FloatSpec s when s.Max is null || field.Value.AsDouble() + 1 <= s.Max => FieldValue.Of(field.Value.AsDouble() + 1),
                _ => null,
            };
            if (next is { } value)
            {
                return [new ValueEdit(field.ValueId, value)];
            }
        }

        return [];
    }

    [Fact]
    public void Info_IsWellFormed()
    {
        var info = Plugin.Info;
        Assert.Matches(PluginIdRegex(), info.Id);
        Assert.False(string.IsNullOrWhiteSpace(info.Name));
        Assert.Matches(SemVerRegex(), info.Version);
        Assert.All(info.Games, g => Assert.Matches(TitleIdRegex(), g.TitleId));
    }

    [Fact]
    public void Probe_ToleratesForeignData()
    {
        byte[][] inputs = [[], [0x00], new byte[4096], [.. Enumerable.Range(0, 4096).Select(i => (byte)(i * 31))], "GVAS"u8.ToArray(), "DSSS\u0002"u8.ToArray(), "{\"a\":1}"u8.ToArray()];
        foreach (var input in inputs)
        {
            var file = MemorySaveFile(input);
            var result = Plugin.Probe(file);
            Assert.True(result.Score >= 0);
        }
    }

    [Fact]
    public void Probe_ClaimsSampleFiles()
    {
        foreach (var title in SampleTitleIds)
        {
            var set = SampleSaves.Load(title);
            Assert.True(SelectFiles(set).Any(), $"{Plugin.Info.Id} claimed no file of sample {title}.");
        }
    }

    [Fact]
    public void Open_BuildsValidSerializableView()
    {
        foreach (var file in AllSelectedFiles())
        {
            using var document = Open(file);
            var fields = Walk(document, file);
            foreach (var field in fields)
            {
                var current = document.GetValue(field.ValueId);
                Assert.True(current == field.Value, $"{file.RelativePath}: GetValue('{field.ValueId}') = {current}, but the view showed {field.Value}.");
                var check = FieldValueCoercer.Normalize(field.Value, field.Spec, allowReadOnly: true);
                Assert.True(check.Ok, $"{file.RelativePath}: field '{field.ValueId}' value {field.Value} violates its own spec: {check.Error}");
            }
        }
    }

    [Fact]
    public void Serialize_WithoutEdits_IsByteIdentical()
    {
        if (!IsWritable)
        {
            return;
        }

        foreach (var file in AllSelectedFiles())
        {
            using var document = Open(file);
            foreach (var output in document.Serialize())
            {
                var original = ((ISaveSet)file.Directory.SaveSet).FindFile(output.RelativePath);
                Assert.True(original is not null, $"Serialize produced unknown path {output.RelativePath}.");
                Assert.True(output.Content.Span.SequenceEqual(original.ReadAllBytes()), $"No-op round trip changed {output.RelativePath}.");
            }
        }
    }

    [Fact]
    public void Edit_SurvivesSerializeAndReopen()
    {
        if (!IsWritable)
        {
            return;
        }

        var tested = 0;
        foreach (var file in AllSelectedFiles())
        {
            using var document = Open(file);
            var fields = Walk(document, file);
            var edits = ChooseEdits(document, fields);
            if (edits.Count == 0)
            {
                continue;
            }

            var specs = fields.ToDictionary(f => f.ValueId, f => f.Spec);
            foreach (var edit in edits)
            {
                var check = FieldValueCoercer.Normalize(edit.Value, specs[edit.ValueId]);
                Assert.True(check.Ok, $"Chosen edit for '{edit.ValueId}' is invalid: {check.Error}");
            }

            var outcome = document.ApplyEdits(edits);
            Assert.True(outcome.Succeeded, $"{file.RelativePath}: edit rejected: {string.Join("; ", outcome.Errors.Select(e => e.Message))}");

            var outputs = document.Serialize();
            Assert.NotEmpty(outputs);
            var overlay = new OverlaySaveSet(file.Directory.SaveSet, outputs.ToDictionary(o => o.RelativePath, o => o.Content));
            var reopenedFile = ((ISaveSet)overlay).FindFile(file.RelativePath)!;
            using var reopened = Open(reopenedFile);
            _ = Walk(reopened, reopenedFile);
            foreach (var edit in edits)
            {
                Assert.Equal(edit.Value, reopened.GetValue(edit.ValueId));
            }

            tested++;
        }

        Assert.True(tested > 0, "No sample produced an editable field; override ChooseEdits or mark the plugin ReadOnly.");
    }

    protected IEnumerable<ISaveFile> AllSelectedFiles()
    {
        var any = false;
        foreach (var title in SampleTitleIds)
        {
            foreach (var file in SelectFiles(SampleSaves.Load(title)))
            {
                any = true;
                yield return file;
            }
        }

        Assert.True(any, "No sample files selected.");
    }

    protected ISaveDocument Open(ISaveFile file)
    {
        try
        {
            return Plugin.Open(file);
        }
        catch (Exception ex)
        {
            throw new Xunit.Sdk.XunitException($"{Plugin.Info.Id} failed to open {file.RelativePath}: {ex}");
        }
    }

    /// <summary>Builds the view, expands lazy sections (within budget), validates and JSON-serialises everything.</summary>
    protected IReadOnlyList<FieldNode> Walk(ISaveDocument document, ISaveFile file)
    {
        var index = new ViewIndex();
        var fields = new List<FieldNode>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var lazy = new Queue<string>();
        var root = document.BuildView();
        Accept(root);

        var budget = LazyExpansionBudget;
        while (lazy.Count > 0 && budget-- > 0)
        {
            foreach (var child in document.ExpandNode(lazy.Dequeue()))
            {
                Accept(child);
            }
        }

        return fields;

        void Accept(ViewNode node)
        {
            index.Add(node);
            _ = JsonSerializer.Serialize(node, ToolkitJson.Options);
            foreach (var n in Flatten(node))
            {
                switch (n)
                {
                    case FieldNode f:
                        Assert.True(seenIds.Add(f.ValueId), $"{file.RelativePath}: value id '{f.ValueId}' appears twice in the view.");
                        fields.Add(f);
                        break;
                    case SectionNode { LazyId: { } id }:
                        lazy.Enqueue(id);
                        break;
                }
            }
        }
    }

    private static IEnumerable<ViewNode> Flatten(ViewNode node)
    {
        yield return node;
        var children = node switch
        {
            SectionNode { LazyId: null } s => s.Children,
            TabsNode t => t.Tabs.SelectMany(tab => tab.Children),
            TableNode table => table.Rows.SelectMany(r => r.Cells.Values),
            _ => [],
        };
        foreach (var child in children.SelectMany(Flatten))
        {
            yield return child;
        }
    }

    private static ISaveFile MemorySaveFile(byte[] content) => MemorySaveSet.SingleFile("data.bin", content);

    [GeneratedRegex("^[a-z0-9]+([.-][a-z0-9]+)*$")]
    private static partial Regex PluginIdRegex();

    [GeneratedRegex(@"^\d+\.\d+\.\d+([-+].+)?$")]
    private static partial Regex SemVerRegex();

    [GeneratedRegex("^[A-Z]{4}[0-9]{5}$")]
    private static partial Regex TitleIdRegex();
}
