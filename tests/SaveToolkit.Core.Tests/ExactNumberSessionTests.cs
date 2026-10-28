using System.Text;
using System.Text.Json;
using SaveToolkit.Core.Export;
using SaveToolkit.Core.Plugins;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Storage;
using SaveToolkit.Core.Tests.Fakes;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class ExactNumberSessionTests : IDisposable
{
    private const string SavePath = "PPSA99998/sdimg_SAVE/test.json";
    private readonly WorkspaceStore _store = new(new PluginCatalog([new ExactJsonPlugin()]),
        new WorkspaceStoreOptions { DataRoot = TestZips.TempDir() });

    public void Dispose() => _store.Dispose();

    private (Workspace Workspace, DocumentSession Session) Open(string json)
    {
        var bytes = TestZips.Build((SavePath, Encoding.UTF8.GetBytes(json)));
        var workspace = _store.Create("exact-json", [new ImportSource("fixture.zip", () => new MemoryStream(bytes))]);
        var session = workspace.Open(workspace.Set.AllDiskFiles.Single().Id, null);
        _ = session.BuildView();
        return (workspace, session);
    }

    private static IncomingEdit Edit(string id, FieldValue value) => new(id, JsonSerializer.SerializeToElement(value, ToolkitJson.Options));

    [Theory]
    [InlineData("12345678901234567890")]
    [InlineData("1.2345678901234567")]
    [InlineData("-0.00000E+03")]
    [InlineData("1e1000000")]
    public void EditWireUndoExportReopen_PreservesExactLexemes(string text)
    {
        var (workspace, session) = Open("{\"number\":0,\"optional\":null}");
        var result = session.ApplyEdits([Edit("number", FieldValue.ExactNumber(text)), Edit("optional", FieldValue.Of(""))]);
        Assert.True(result.Ok);
        var response = JsonSerializer.SerializeToElement(result, ToolkitJson.Options);
        Assert.Equal(text, response.GetProperty("changes")[0].GetProperty("newValue").GetProperty("text").GetString());
        Assert.True(session.Undo().Ok);
        Assert.False(session.State.IsDirty);
        Assert.True(session.Redo().Ok);
        var plan = WorkspaceExporter.Plan(workspace, new ExportOptions { Scope = ExportScope.ChangedFiles, MirrorBackups = false });
        var bytes = plan.Overrides.Values.Single().ToArray();
        using var save = JsonDocument.Parse(bytes);
        Assert.Equal(text, save.RootElement.GetProperty("number").GetRawText());
        Assert.Equal("", save.RootElement.GetProperty("optional").GetString());
        var (_, reopened) = Open(Encoding.UTF8.GetString(bytes));
        var fields = ((SectionNode)reopened.BuildView()).Children.OfType<FieldNode>().ToDictionary(f => f.ValueId);
        Assert.Equal(FieldValue.ExactNumber(text), fields["number"].Value);
        Assert.Equal(FieldValue.Of(""), fields["optional"].Value);
        Assert.True(session.ApplyEdits([Edit("optional", FieldValue.Null)]).Ok);
        using var nullSave = JsonDocument.Parse(session.Serialize().Single().Content);
        Assert.Equal(JsonValueKind.Null, nullSave.RootElement.GetProperty("optional").ValueKind);
    }

    [Fact]
    public void MissingAndInvalidEdits_LeaveSaveAndHistoryUnchanged()
    {
        var (_, session) = Open("{\"number\":0}");
        var result = session.ApplyEdits([Edit("number", FieldValue.ExactNumber("2")), Edit("optional", FieldValue.Null)]);
        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.ValueId == "optional");
        Assert.False(session.State.IsDirty);
        Assert.Empty(session.Serialize());
        var badNumber = JsonSerializer.SerializeToElement(new { kind = "exactNumber", text = "01" });
        Assert.False(session.ApplyEdits([new IncomingEdit("number", badNumber)]).Ok);
        Assert.False(session.ApplyEdits([new IncomingEdit("number", default)]).Ok);
        Assert.Equal(FieldValue.ExactNumber("0"), ((SectionNode)session.BuildView()).Children.OfType<FieldNode>().Single().Value);
    }

    [Fact]
    public void UnionTypeChoice_SurvivesUndoAndReopen()
    {
        var (workspace, session) = Open("{\"choice\":{\"kind\":\"union\",\"type\":\"text\",\"value\":\"12\"}}");
        var next = FieldValue.Union("integer", FieldValue.Of(12L));
        Assert.True(session.ApplyEdits([Edit("choice", next)]).Ok);
        Assert.True(session.Undo().Ok);
        Assert.Equal(FieldValue.Union("text", FieldValue.Of("12")), ((SectionNode)session.BuildView()).Children.OfType<FieldNode>().Single().Value);
        Assert.True(session.Redo().Ok);
        var plan = WorkspaceExporter.Plan(workspace, new ExportOptions { Scope = ExportScope.ChangedFiles, MirrorBackups = false });
        var (_, reopened) = Open(Encoding.UTF8.GetString(plan.Overrides.Values.Single().Span));
        var field = ((SectionNode)reopened.BuildView()).Children.OfType<FieldNode>().Single();
        Assert.Equal(next, field.Value);
    }
}
