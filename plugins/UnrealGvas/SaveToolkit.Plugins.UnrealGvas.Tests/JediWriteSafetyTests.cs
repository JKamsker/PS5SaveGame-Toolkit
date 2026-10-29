using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class JediWriteSafetyTests
{
    [Fact]
    public void ActorPayloadLengthCannotBeChangedIndependentlyOfTheCompressedStream()
    {
        var sample = SampleSaves.Load("PPSA02199").AllDiskFiles.First(file =>
            file.Directory.Name == "SaveGame00" && file.FileName == "ue4savegame.dpx.sav");
        using var document = new UnrealGvasPlugin().Open(sample);
        var fields = GvasFormatTests.Expand(document);
        var size = fields.Single(field => field.Label.StartsWith("UncompressedActorRecordsSize ·", StringComparison.Ordinal));

        var outcome = document.ApplyEdits([new ValueEdit(size.ValueId, FieldValue.Of(1L))]);

        Assert.False(outcome.Succeeded);
        Assert.True(size.Spec.ReadOnly);
        Assert.Equal(size.Value, document.GetValue(size.ValueId));
        Assert.Equal(sample.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Contains(fields, field => !field.Spec.ReadOnly);
        var parsed = GvasFile.Parse(Assert.Single(document.Serialize()).Content.Span);
        Assert.Equal((Int128)size.Value.AsInteger(), parsed.Properties.Single(node => node.Name == "UncompressedActorRecordsSize").Scalar!.Value);
    }
}
