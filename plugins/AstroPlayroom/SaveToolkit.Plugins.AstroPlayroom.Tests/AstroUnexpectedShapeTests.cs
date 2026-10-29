using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroUnexpectedShapeTests
{
    [Theory]
    [InlineData("World", "true")]
    [InlineData("World", "false")]
    [InlineData("Progress", "true")]
    [InlineData("Progress", "false")]
    public void BooleanGroupScalarRemainsInspectableReadOnlyAndLossless(string group, string text)
    {
        var bytes = AstroTestData.Bytes($"\r\n<PreInstall><{group}>{text}</{group}></PreInstall>");
        using var document = AstroTestData.Open(bytes);
        var field = AstroTestData.Find(document, $"PreInstall/{group}");
        Assert.True(field.Spec.ReadOnly);
        Assert.Equal(Confidence.Unknown, field.Confidence);
        Assert.Equal(FieldValue.Of(text == "true"), field.Value);
        var rejected = document.ApplyEdits([new ValueEdit(field.ValueId, FieldValue.Of(text != "true"))]);
        Assert.False(rejected.Succeeded);
        Assert.Equal(field.Value, document.GetValue(field.ValueId));
        Assert.Equal(bytes, AstroTestData.Output(document));
    }

    [Theory]
    [InlineData("World")]
    [InlineData("Progress")]
    public void NestedBooleanFlagRemainsEditable(string group)
    {
        var bytes = AstroTestData.Bytes($"\r\n<PreInstall><{group}><Flag>false</Flag></{group}></PreInstall>");
        using var document = AstroTestData.Open(bytes);
        var field = AstroTestData.Find(document, $"PreInstall/{group}/Flag");
        Assert.False(field.Spec.ReadOnly);
        Assert.Equal(Confidence.Inferred, field.Confidence);
        Assert.True(document.ApplyEdits([new ValueEdit(field.ValueId, FieldValue.Of(true))]).Succeeded);
        using var reopened = AstroTestData.Open(AstroTestData.Output(document));
        Assert.Equal(FieldValue.Of(true), AstroTestData.Find(reopened, $"PreInstall/{group}/Flag").Value);
    }
}
