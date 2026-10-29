using System.Text;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.GenericJson.Tests;

public sealed class JsonSemanticSampleTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("PPSA04027", "settings.json")]
    [InlineData("PPSA02343", "ue4savegame.dpx.sav")]
    [InlineData("PPSA07696", "imgui_emu_option.json")]
    public void EverySuppliedTitleAdapterPersistsAndPreservesUnrelatedBytesWithUndoRedo(string title, string name)
    {
        var tested = 0;
        var singleValueRanges = 0;
        var files = SampleSaves.Load(title).AllDiskFiles.Where(f => f.FileName == name).ToArray();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var source = file.ReadAllBytes();
            using var fixture = new JsonSessionFixture(Encoding.UTF8.GetString(source), title: title, fileName: name);
            var actions = JsonSemanticTestHelper.Actions(fixture).Where(a => a.ActionId.StartsWith("semantic/", StringComparison.Ordinal)
                && a.ActionId.EndsWith("/set", StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(actions);
            foreach (var action in actions)
            {
                var parameter = action.Parameters[0];
                FieldValue next;
                switch (parameter.Spec)
                {
                    case BoolSpec: next = FieldValue.Of(!parameter.Value.AsBool()); break;
                    case IntegerSpec integer:
                        next = FieldValue.Of(parameter.Value.AsInteger() == 0 ? Int128.Min(1, integer.Max) : 0);
                        break;
                    case ExactNumberSpec exact:
                        if (exact.Min == exact.Max) { singleValueRanges++; continue; }
                        next = FieldValue.ExactNumber(parameter.Value.AsExactNumber() == exact.Min ? exact.Max! : exact.Min!);
                        break;
                    case EnumSpec choices:
                        if (choices.Options.All(o => o.Value == parameter.Value.AsString())) { singleValueRanges++; continue; }
                        next = FieldValue.Of(choices.Options.First(o => o.Value != parameter.Value.AsString()).Value);
                        break;
                    case TextSpec:
                        if (parameter.Value.AsString().Length == 0) continue;
                        next = FieldValue.Of(parameter.Value.AsString()[..^1]);
                        break;
                    default: throw new InvalidOperationException("Unexpected semantic adapter spec.");
                }
                var parameters = new List<(string, FieldValue)> { ("value", next) };
                parameters.AddRange(action.Parameters.Skip(1).Select(p => (p.ValueId, p.Value)));
                JsonSemanticTestHelper.ApplyAndVerify(fixture, action.ActionId, parameters.ToArray());
                Assert.True(fixture.Session.Undo().Ok);
                Assert.True(source.AsSpan().SequenceEqual(fixture.Bytes));
                tested++;
            }
        }
        Assert.True(tested > 0);
        output.WriteLine($"{title}: {tested} adapter edits across every primary/backup generation passed persistence, F1 token preservation and undo/redo; {singleValueRanges} single-value conservative ranges retained.");
    }

    [Theory]
    [InlineData("PPSA04027", "settings.json", "semantic/defaults")]
    [InlineData("PPSA02343", "ue4savegame.dpx.sav", "semantic/reset-counters")]
    [InlineData("PPSA02343", "ue4savegame.dpx.sav", "semantic/present-flags")]
    [InlineData("PPSA02343", "ue4savegame.dpx.sav", "semantic/progress")]
    [InlineData("PPSA07696", "imgui_emu_option.json", "semantic/wrapper-imported")]
    public void RealSamplePresetsPlanSkippedRowsAndApplyAsOneUndoUnit(string title, string name, string id)
    {
        var edits = 0;
        var files = SampleSaves.Load(title).AllDiskFiles.Where(f => f.FileName == name).ToArray();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            using var fixture = new JsonSessionFixture(Encoding.UTF8.GetString(file.ReadAllBytes()), title: title, fileName: name);
            var actions = JsonSemanticTestHelper.Actions(fixture);
            if (id == "semantic/wrapper-imported")
            {
                var boolean = actions.First(a => a.ActionId.StartsWith("semantic/", StringComparison.Ordinal) && a.Parameters.FirstOrDefault()?.Spec is BoolSpec);
                JsonSemanticTestHelper.ApplyAndVerify(fixture, boolean.ActionId, ("value", FieldValue.Of(!boolean.Parameters[0].Value.AsBool())));
            }
            var parameters = id == "semantic/present-flags" ? new[] { ("enabled", FieldValue.Of(false)) }
                : id == "semantic/progress" ? actions.Single(a => a.ActionId == id).Parameters
                    .Select(p => (p.ValueId, FieldValue.Of(p.Value.AsString()[..^1]))).ToArray() : [];
            var preview = fixture.Session.PlanAction(id, JsonArrayEditingTests.Parameters(parameters), expectedRevision: fixture.Session.State.Revision, scope: null, cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotNull(preview.PlanId);
            Assert.NotEmpty(preview.Plan.Warnings);
            if (preview.Plan.Changes.Count == 0) continue;
            JsonSemanticTestHelper.ApplyAndVerify(fixture, id, parameters);
            edits++;
        }
        Assert.True(edits > 0);
    }
}
