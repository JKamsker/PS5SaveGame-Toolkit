using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Insomniac.Tests;

public sealed class DdlBudgetTests
{
    private const int MaximumValues = 524288;
    private const int MaximumTextBytes = 4 * 1024 * 1024;
    private const int MaximumStringBytes = 65536;
    private const int MaximumObjects = 32768;
    private const int MaximumFieldsPerObject = 1024;
    private const int MaximumTotalFields = 65536;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FieldBudgetCountsDescriptorsAcrossEveryChildObject(bool exceedsBudget)
    {
        var fields = Enumerable.Range(0, MaximumFieldsPerObject).Select(index =>
            new DdlCompoundFixture.Field($"Value{index}", DdlValueKind.UInt8, [1])).ToArray();
        var children = Enumerable.Repeat(fields, MaximumTotalFields / MaximumFieldsPerObject).ToArray();
        // The root's Children descriptor consumes one slot as well.
        if (!exceedsBudget) children[^1] = fields[..^1];
        var fixture = new DdlCompoundFixture(DdlCompoundFixture.Field.Objects("Children", children));
        if (exceedsBudget)
            Assert.Contains("Total field budget exceeded", Assert.Throws<SaveFormatException>(() => NestedDdlTests.Read(fixture.Bytes)).Message);
        else
            Assert.Equal(MaximumTotalFields - 1, NestedDdlTests.Read(fixture.Bytes)["Children"].Objects.Sum(child => child.Fields.Count));
    }

    [Theory]
    [InlineData(MaximumValues, false)]
    [InlineData(MaximumValues + 1, true)]
    public void CombinedValueCountsEnforceTheLimitAcrossFields(int total, bool rejects)
    {
        var fixture = new DdlTestFixture(
            new("First", DdlValueKind.UInt8, new byte[total - 1], Count: total - 1),
            new("Last", DdlValueKind.UInt8, [1]));
        if (rejects)
            Assert.Contains("Unsupported field count or array kind", Assert.Throws<SaveFormatException>(() => NestedDdlTests.Read(fixture.Bytes)).Message);
        else
        {
            var fields = NestedDdlTests.Read(fixture.Bytes);
            Assert.Equal(total - 1, fields["First"].Values.Count);
            Assert.Equal(1, Assert.Single(fields["Last"].Values).AsInt64());
        }
    }

    [Theory]
    [InlineData(MaximumObjects - 1, false)]
    [InlineData(MaximumObjects, true)]
    public void ObjectArraysIncludeTheRootInTheirNodeBudget(int children, bool rejects)
    {
        const int emptyObjectSize = 16;
        var payload = new byte[children * emptyObjectSize];
        for (var offset = 0; offset < payload.Length; offset += emptyObjectSize)
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset + 4), 0x03150044);
        var fixture = new DdlTestFixture(new DdlTestFixture.Field("Children", DdlValueKind.Object, payload, children));
        if (rejects)
            Assert.Contains("Object depth or node budget exceeded", Assert.Throws<SaveFormatException>(() => NestedDdlTests.Read(fixture.Bytes)).Message);
        else Assert.Equal(children, NestedDdlTests.Read(fixture.Bytes)["Children"].Objects.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StringArraysAndNamesShareOneTextBudget(bool exceedsBudget)
    {
        const string name = "Strings";
        const int fullStrings = MaximumTextBytes / MaximumStringBytes - 1;
        var lastLength = MaximumStringBytes - name.Length + (exceedsBudget ? 1 : 0);
        var full = DdlTestFixture.StringPayload(new string('a', MaximumStringBytes));
        var payload = Enumerable.Range(0, fullStrings).SelectMany(_ => full)
            .Concat(DdlTestFixture.StringPayload(new string('b', lastLength))).ToArray();
        var fixture = new DdlTestFixture(new DdlTestFixture.Field(name, DdlValueKind.String, payload, fullStrings + 1));
        if (exceedsBudget)
            Assert.Contains("Text budget exceeded", Assert.Throws<SaveFormatException>(() => NestedDdlTests.Read(fixture.Bytes)).Message);
        else
        {
            var values = NestedDdlTests.Read(fixture.Bytes)[name].Values;
            Assert.Equal(fullStrings + 1, values.Count);
            Assert.Equal(lastLength, values[^1].AsString().Length);
        }
    }
}
