using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Core.Tests;

public sealed class FieldValueCoercerTests
{
    private static FieldValueCoercer.Result Coerce(string json, FieldSpec spec) =>
        FieldValueCoercer.FromJson(JsonDocument.Parse(json).RootElement, spec);

    [Theory]
    [InlineData("\"18446744073709551615\"", true)]
    [InlineData("\"18446744073709551616\"", false)]
    [InlineData("\"-1\"", false)]
    [InlineData("\"0xFF\"", true)]
    [InlineData("42", true)]
    [InlineData("4.5", false)]
    [InlineData("true", false)]
    public void Integer_RespectsUInt64Range(string json, bool ok) => Assert.Equal(ok, Coerce(json, IntegerSpec.UInt64).Ok);

    [Fact]
    public void Integer_ParsesHex() => Assert.Equal(255, Coerce("\"0xff\"", IntegerSpec.UInt8).Value.AsInt32());

    [Fact]
    public void Float_SinglePrecision_RoundsToFloat32()
    {
        var result = Coerce("0.1", FloatSpec.Single);

        Assert.True(result.Ok);
        Assert.Equal((double)0.1f, result.Value.AsDouble());
    }

    [Theory]
    [InlineData("\"NaN\"")]
    [InlineData("1e300")]
    public void Float_RejectsNonFiniteAndOverflow(string json) => Assert.False(Coerce(json, FloatSpec.Single).Ok);

    [Fact]
    public void Text_EnforcesMaxLengthAndLineBreaks()
    {
        var spec = new TextSpec { MaxLength = 3 };

        Assert.True(Coerce("\"abc\"", spec).Ok);
        Assert.False(Coerce("\"abcd\"", spec).Ok);
        Assert.False(Coerce("\"a\\nb\"", spec).Ok);
        Assert.True(Coerce("\"a\\nb\"", new TextSpec { Multiline = true }).Ok);
    }

    [Fact]
    public void Enum_RequiresKnownOptionUnlessCustomAllowed()
    {
        var spec = new EnumSpec([new EnumOption("1", "Easy"), new EnumOption("2", "Hard")]);

        Assert.True(Coerce("\"2\"", spec).Ok);
        Assert.False(Coerce("\"3\"", spec).Ok);
        Assert.True(Coerce("\"3\"", spec with { AllowCustom = true }).Ok);
    }

    [Fact]
    public void Flags_LimitedToBitWidth()
    {
        var spec = new FlagsSpec([new FlagBit(0, "A")], 4);

        Assert.True(Coerce("\"15\"", spec).Ok);
        Assert.False(Coerce("\"16\"", spec).Ok);
    }

    [Fact]
    public void DateTime_NormalisesIso()
    {
        var result = Coerce("\"2026-10-26T12:30:00\"", new DateTimeSpec());

        Assert.Equal("2026-10-26T12:30:00.0000000", result.Value.AsString());
        Assert.False(Coerce("\"yesterday\"", new DateTimeSpec()).Ok);
    }

    [Fact]
    public void ReadOnly_IsRejected() => Assert.False(Coerce("true", new BoolSpec { ReadOnly = true }).Ok);
}
