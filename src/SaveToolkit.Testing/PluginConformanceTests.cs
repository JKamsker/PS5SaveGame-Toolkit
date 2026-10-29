using System.Text.RegularExpressions;
using SaveToolkit.Core.Editing;
using SaveToolkit.Core.References;
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
    private const int DefaultQueryPageBudget = 12;

    protected TPlugin Plugin { get; } = new();

    /// <summary>Title ids whose sample zips contain files this plugin must open.</summary>
    protected abstract IReadOnlyList<string> SampleTitleIds { get; }

    /// <summary>Caps how many distinct files per title are opened (big titles are slow).</summary>
    protected virtual int MaxFilesPerTitle => 6;

    /// <summary>Maximum number of lazy sections expanded while walking a view.</summary>
    protected virtual int LazyExpansionBudget => 150;

    /// <summary>Maximum number of F8 pages sampled across all collections while walking a view.</summary>
    protected virtual int QueryPageBudget => DefaultQueryPageBudget;

    protected virtual bool IsWritable => Plugin.Info.Maturity != PluginMaturity.ReadOnly;

    /// <summary>Opt into failing mechanical label checks after reviewing document-level warnings and exceptions.</summary>
    protected virtual bool EnforceHonestLabelAdvisories => false;

    /// <summary>Return a nonempty, evidence-backed exception reason; null leaves the advisory unresolved.</summary>
    protected virtual string? HonestLabelException(HonestLabelFinding finding) => null;

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
    public void Open_ReportsHonestLabelAdvisories()
    {
        foreach (var file in AllSelectedFiles())
        {
            using var document = Open(file);
            HonestLabelConformance.Check(document, file.RelativePath, Plugin.Info.Maturity,
                LazyExpansionBudget, QueryPageBudget, HonestLabelException, EnforceHonestLabelAdvisories);
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
                // Existing ids may be unknown or no longer eligible. A view must preserve those,
                // while incoming replacements are checked against the catalog below.
                var check = field.Spec is ReferenceSpec
                    ? new FieldValueCoercer.Result(field.Value, field.Value.Kind == FieldValueKind.Text ? null : "Reference ids must be text.")
                    : FieldValueCoercer.Normalize(field.Value, field.Spec, allowReadOnly: true);
                Assert.True(check.Ok, $"{file.RelativePath}: field '{field.ValueId}' value {field.Value} violates its own spec: {check.Error}");
            }
        }
    }

    [Fact]
    public void Serialize_WithoutEdits_IsByteIdentical()
    {
        // Applies to read-only plugins too: whatever Serialize returns must not alter the data.
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
            using var document = Open(file, out var referenceFields);
            var fields = Walk(document, file);
            var edits = ChooseEdits(document, fields);
            if (edits.Count == 0)
            {
                continue;
            }

            var specs = fields.ToDictionary(f => f.ValueId, f => f.Spec);
            foreach (var edit in edits)
            {
                var check = FieldValueCoercer.Normalize(edit.Value, specs[edit.ValueId], references: referenceFields);
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

    protected ISaveDocument Open(ISaveFile file) => Open(file, out _);

    private ISaveDocument Open(ISaveFile file, out ReferenceFieldService referenceFields)
    {
        var references = (ReferenceContext)ReferencesFor(file);
        referenceFields = new ReferenceFieldService(references);
        try
        {
            return Plugin.Open(file, references);
        }
        catch (Exception ex)
        {
            throw new Xunit.Sdk.XunitException($"{Plugin.Info.Id} failed to open {file.RelativePath}: {ex}");
        }
    }

    /// <summary>Validates and JSON-serialises eager views, lazy sections and sampled F8 pages.</summary>
    protected IReadOnlyList<FieldNode> Walk(ISaveDocument document, ISaveFile file) =>
        new ConformanceViewWalker(document, file.RelativePath, LazyExpansionBudget, QueryPageBudget).Walk();

    private static ISaveFile MemorySaveFile(byte[] content) => MemorySaveSet.SingleFile("data.bin", content);

    [GeneratedRegex("^[a-z0-9]+([.-][a-z0-9]+)*$")]
    private static partial Regex PluginIdRegex();

    [GeneratedRegex(@"^\d+\.\d+\.\d+([-+].+)?$")]
    private static partial Regex SemVerRegex();

    [GeneratedRegex("^[A-Z]{4}[0-9]{5}$")]
    private static partial Regex TitleIdRegex();
}
