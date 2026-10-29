using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class GvasWriteSafetyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GenericAndSettingsFallbacksWarnWithoutChangingStorageConfidence(bool settings)
    {
        const string consoleWarning = "Not validated on console. The game may reject or overwrite the edited save.";
        const string storageWarning = "Serialized property name and type are verified. Game constraints and effects are unverified; keep a backup.";
        var file = settings
            ? SampleSaves.Load("PPSA02199").AllDiskFiles.First(file =>
                file.Directory.Name == "GameUserSettings" && file.FileName == "ue4savegame.dpx.sav")
            : MemorySaveSet.SingleFile("generic.sav", GvasFixture.Nested());
        using var document = new UnrealGvasPlugin().Open(file);
        Assert.True(document.CanEdit);
        var overview = Assert.Single(Assert.IsType<TabsNode>(document.BuildView()).Tabs, tab => tab.Title == "Overview");
        var warning = Assert.Single(overview.Children.OfType<TextNode>(), node => node.Tone == Tone.Warning);
        Assert.StartsWith(consoleWarning, warning.Text, StringComparison.Ordinal);
        Assert.Contains("Embedded compressed payloads and unknown native structures are preserved read-only.", warning.Text, StringComparison.Ordinal);

        var editable = GvasFormatTests.Expand(document).Where(field => !field.Spec.ReadOnly).ToArray();
        Assert.NotEmpty(editable);
        Assert.All(editable, field =>
        {
            Assert.Equal(Confidence.Verified, field.Confidence);
            Assert.Equal(consoleWarning + " " + storageWarning, field.Warning);
        });
        Assert.Equal(file.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportedEmbeddedNulStringCanBeRestoredWithoutPermittingNewNulStrings(bool wide)
    {
        const string imported = "before\0suffix";
        byte[] bytes = GvasFixture.Build(writer => GvasFixture.Property(writer, "Name", "StrProperty",
            GvasFixture.Body(body => GvasFixture.String(body, imported, wide))));
        using var document = new UnrealGvasPlugin().Open(MemorySaveSet.SingleFile("text.sav", bytes));
        var field = Assert.Single(GvasFormatTests.Expand(document));
        var edited = document.ApplyEdits([new(field.ValueId, FieldValue.Of("renamed"))]);
        Assert.True(edited.Succeeded);
        byte[] renamed = Assert.Single(document.Serialize()).Content.ToArray();
        Assert.False(document.ApplyEdits([new(field.ValueId, FieldValue.Of("different\0text"))]).Succeeded);
        Assert.Equal(renamed, Assert.Single(document.Serialize()).Content.ToArray());

        var restored = document.ApplyEdits([new(field.ValueId, Assert.Single(edited.Changes).OldValue)]);

        Assert.True(restored.Succeeded, string.Join("; ", restored.Errors.Select(error => error.Message)));
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(document.ApplyEdits([new(field.ValueId, field.Value)]).Succeeded);
        Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.True(document.ApplyEdits([new(field.ValueId, FieldValue.Of("renamed"))]).Succeeded);
        Assert.Equal(renamed, Assert.Single(document.Serialize()).Content.ToArray());
    }
}
