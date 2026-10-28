using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class GvasVersionTests
{
    private static readonly string[] KnownStructs = ["Vector", "Rotator", "Vector2D", "Quat", "LinearColor", "Guid", "DateTime", "Timespan", "Color"];
    private static readonly string[] CompleteNativeStructs = ["Vector", "FutureNative"];
    [Theory]
    [InlineData(4, 27, false)]
    [InlineData(5, 3, true)]
    public void KnownStructs_UseCorrectCoordinatePrecisionAndPreserveFixedLayouts(int major, int minor, bool doubles)
    {
        var bytes = GvasFixture.Build(w =>
        {
            foreach (var type in KnownStructs)
            {
                var body = GvasFixture.Body(b =>
                {
                    var count = type switch { "Vector" or "Rotator" => 3, "Vector2D" => 2, "DateTime" or "Timespan" => 1, _ => 4 };
                    for (var i = 0; i < count; i++)
                    {
                        if (type == "Guid") b.Write((uint)i);
                        else if (type is "DateTime" or "Timespan") b.Write(123456789L);
                        else if (type == "Color") b.Write((byte)i);
                        else if (type != "LinearColor" && doubles) b.Write((double)i + 0.5);
                        else b.Write(i + 0.5f);
                    }
                });
                GvasFixture.Property(w, type, "StructProperty", body, type);
            }
        }, major, minor);
        var parsed = GvasFile.Parse(bytes);
        Assert.Equal(bytes, parsed.Serialize());
        Assert.All(parsed.Properties, n => Assert.NotEmpty(n.Children));
        var vector = parsed.Properties.Single(n => n.Name == "Vector").Children[0];
        Assert.Equal(doubles ? 8 : 4, vector.Scalar!.Length);
        vector.Scalar.Value = 42.25;
        Assert.Equal(42.25, GvasFile.Parse(parsed.Serialize()).Properties.Single(n => n.Name == "Vector").Children[0].Scalar!.Value);
    }

    [Fact]
    public void Ue54CompleteTypeTags_PreserveFlagsGuidAndReadPrimitiveValues()
    {
        var bytes = GvasFixture.Build(w =>
        {
            GvasFixture.String(w, "Value");
            GvasFixture.String(w, "IntProperty");
            w.Write(0); // No type parameters.
            w.Write(4); // Payload size.
            w.Write((byte)3); // Static array index and GUID.
            w.Write(9);
            w.Write(Enumerable.Range(0, 16).Select(i => (byte)i).ToArray());
            w.Write(71);
            GvasFixture.String(w, "Flag");
            GvasFixture.String(w, "BoolProperty");
            w.Write(0); w.Write(0); w.Write((byte)19);
            w.Write(3); w.Write(new byte[16]); // Other flag bits must survive a bool edit.
        }, 5, 4);
        var parsed = GvasFile.Parse(bytes);
        Assert.Equal(1009, parsed.Header.PackageVersionUe5);
        Assert.Equal((Int128)71, parsed.Properties[0].Scalar!.Value);
        Assert.Equal(true, parsed.Properties[1].Scalar!.Value);
        Assert.Equal(bytes, parsed.Serialize());
        parsed.Properties[0].Scalar!.Value = (Int128)80;
        parsed.Properties[1].Scalar!.Value = false;
        var reopened = GvasFile.Parse(parsed.Serialize());
        Assert.Equal((Int128)80, reopened.Properties[0].Scalar!.Value);
        Assert.Equal(false, reopened.Properties[1].Scalar!.Value);
    }

    [Fact]
    public void Ue54NativeVectorAndOpaqueNativeStruct_KeepCompleteTypeNamesAndVariableSizes()
    {
        var bytes = GvasFixture.Build(w =>
        {
            foreach (var type in CompleteNativeStructs)
            {
                GvasFixture.String(w, type);
                GvasFixture.String(w, "StructProperty"); w.Write(1);
                GvasFixture.String(w, type); w.Write(1);
                GvasFixture.String(w, "/Script/CoreUObject"); w.Write(0);
                w.Write(24); w.Write((byte)8);
                w.Write(1.25); w.Write(2.5); w.Write(3.75);
            }
            GvasFixture.String(w, "Name");
            GvasFixture.String(w, "StrProperty"); w.Write(0);
            var body = GvasFixture.Body(b => GvasFixture.String(b, "Short"));
            w.Write(body.Length); w.Write((byte)0); w.Write(body);
        }, 5, 4);
        var parsed = GvasFile.Parse(bytes);
        Assert.Equal(3, parsed.Properties[0].Children.Count);
        Assert.Empty(parsed.Properties[1].Children);
        Assert.Contains("native", parsed.Properties[1].Note, StringComparison.Ordinal);
        Assert.Equal(bytes, parsed.Serialize());
        parsed.Properties[0].Children[0].Scalar!.Value = 9.5;
        parsed.Properties[2].Scalar!.Value = "A much longer name in UE5.4";
        var reopened = GvasFile.Parse(parsed.Serialize());
        Assert.Equal(9.5, reopened.Properties[0].Children[0].Scalar!.Value);
        Assert.Equal("A much longer name in UE5.4", reopened.Properties[2].Scalar!.Value);
    }

    [Fact]
    public void BoundedViews_PageLargeCollectionsAndExposeOpaqueHex()
    {
        var bytes = GvasFixture.Build(w =>
        {
            GvasFixture.Property(w, "Numbers", "ArrayProperty", GvasFixture.Body(b =>
            {
                b.Write(501);
                for (var i = 0; i < 501; i++) b.Write(i);
            }), "IntProperty");
            GvasFixture.Property(w, "Raw", "ArrayProperty", GvasFixture.Body(b => { b.Write(3); b.Write(new byte[] { 7, 8, 9 }); }), "ByteProperty");
        });
        using var document = new UnrealGvasPlugin().Open(SaveToolkit.Testing.MemorySaveSet.SingleFile("save", bytes));
        var fields = GvasFormatTests.Expand(document);
        Assert.Equal(501, fields.Count);
        Assert.True(document.ReadBlob("property/1", 4, 100).Span.SequenceEqual(new byte[] { 7, 8, 9 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ReadBlob("property/1", -1, 1));
    }
}
