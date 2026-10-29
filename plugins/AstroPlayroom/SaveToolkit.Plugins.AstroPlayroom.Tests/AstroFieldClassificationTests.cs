using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroFieldClassificationTests
{
    [Theory]
    [InlineData("<Common><NumCoin>+0007</NumCoin></Common>", "Common/NumCoin", FieldValueKind.Integer, false, Confidence.Verified)]
    [InlineData("<Common><PlayCount>2147483647</PlayCount></Common>", "Common/PlayCount", FieldValueKind.Integer, false, Confidence.Verified)]
    [InlineData("<Common><NumCoin>-1</NumCoin></Common>", "Common/NumCoin", FieldValueKind.Integer, true, Confidence.Unknown)]
    [InlineData("<Common><NumCoin>2147483648</NumCoin></Common>", "Common/NumCoin", FieldValueKind.Integer, true, Confidence.Unknown)]
    [InlineData("<Common><NumCoin>9223372036854775808</NumCoin></Common>", "Common/NumCoin", FieldValueKind.Text, true, Confidence.Unknown)]
    [InlineData("<Common><NumCoin>1.0</NumCoin></Common>", "Common/NumCoin", FieldValueKind.Text, true, Confidence.Unknown)]
    [InlineData("<Common><NumCoin>true</NumCoin></Common>", "Common/NumCoin", FieldValueKind.Bool, true, Confidence.Unknown)]
    [InlineData("<Common><NumCoin n='2'>1</NumCoin></Common>", "Common/NumCoin", FieldValueKind.Integer, false, Confidence.Verified)]
    [InlineData("<Common><NumCoin n='2'>1</NumCoin></Common>", "Common/NumCoin/@n", FieldValueKind.Integer, true, Confidence.Unknown)]
    [InlineData("<Common><NumCoin>1<N/></NumCoin></Common>", "Common/NumCoin", FieldValueKind.Integer, true, Confidence.Unknown)]
    [InlineData("<World>true</World>", "World", FieldValueKind.Bool, true, Confidence.Unknown)]
    [InlineData("<Progress>false</Progress>", "Progress", FieldValueKind.Bool, true, Confidence.Unknown)]
    [InlineData("<World><Flag>false</Flag></World>", "World/Flag", FieldValueKind.Bool, false, Confidence.Inferred)]
    [InlineData("<Progress><Flag>true</Flag></Progress>", "Progress/Flag", FieldValueKind.Bool, false, Confidence.Inferred)]
    [InlineData("<World><Flag>True</Flag></World>", "World/Flag", FieldValueKind.Text, true, Confidence.Unknown)]
    [InlineData("<World><Flag v='true'/></World>", "World/Flag/@v", FieldValueKind.Bool, true, Confidence.Unknown)]
    [InlineData("<Artifact><A><IsGot>false</IsGot></A></Artifact>", "Artifact/A/IsGot", FieldValueKind.Bool, false, Confidence.Inferred)]
    [InlineData("<Puzzle><A><IsCinematicsPlayed>true</IsCinematicsPlayed></A></Puzzle>", "Puzzle/A/IsCinematicsPlayed", FieldValueKind.Bool, false, Confidence.Inferred)]
    [InlineData("<Prop><A><Other>false</Other></A></Prop>", "Prop/A/Other", FieldValueKind.Bool, true, Confidence.Unknown)]
    public void ScalarTypeAndEditingEligibilityRemainIndependentOfPresentation(
        string markup, string path, FieldValueKind kind, bool readOnly, Confidence confidence)
    {
        var bytes = AstroTestData.Bytes($"\r\n<PreInstall>{markup}</PreInstall>opaque");
        using var document = AstroTestData.Open(bytes);
        var field = AstroTestData.Find(document, "PreInstall/" + path);
        Assert.Equal(kind, field.Value.Kind);
        Assert.Equal(readOnly, field.Spec.ReadOnly);
        Assert.Equal(confidence, field.Confidence);
        Assert.Equal(field.Value, document.GetValue(field.ValueId));
        Assert.Equal(bytes, AstroTestData.Output(document));
    }
}
