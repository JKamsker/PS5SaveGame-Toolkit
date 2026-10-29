using System.Buffers.Binary;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class LsfAllocationTests
{
    private const int AttributeCount = 10_000;
    private const int MaximumBytesPerAttribute = 128;

    [Fact]
    public void ValidatingFixedWidthValuesDoesNotAllocateFailureMessages()
    {
        var attribute = new ResourceAttribute("Counter", LarianAttributeType.Int, new byte[sizeof(int)]);
        AttributeDecoder.Validate(attribute);
        // Minimum of three runs, bounded below one byte per call: a per-call message would cost at least
        // 24 bytes per validation, while tiered-JIT/OSR transitions add a few KB to a single run.
        var allocated = long.MaxValue;
        for (var run = 0; run < 3; run++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var index = 0; index < AttributeCount; index++) AttributeDecoder.Validate(attribute);
            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.True(allocated < AttributeCount, $"Successful validation allocated {allocated:N0} diagnostic bytes.");
    }

    [Fact]
    public void ValidAttributeTableAllocatesTheModelWithoutPerRowDiagnostics()
    {
        var nodes = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(nodes.AsSpan(8), -1);
        var attributes = new byte[AttributeCount * 12];
        for (var index = 0; index < AttributeCount; index++)
            BinaryPrimitives.WriteUInt32LittleEndian(attributes.AsSpan(index * 12 + 4), (sizeof(int) << 6) | 4u);
        var values = new byte[AttributeCount * sizeof(int)];
        var names = new Dictionary<uint, string> { [0] = "Counter" };
        _ = LsfTables.Read(names, nodes, attributes, values, [], false);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var model = LsfTables.Read(names, nodes, attributes, values, [], false);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.Equal(AttributeCount, model.Attributes.Count);
        Assert.Equal(AttributeCount, model.Nodes[0].Attributes.Count);
        Assert.True(allocated < AttributeCount * MaximumBytesPerAttribute,
            $"Parsing allocated {allocated:N0} bytes; valid attributes must not format error messages.");
    }

    [Fact]
    public void InvalidAttributeRetainsItsTypeSpecificDiagnostic()
    {
        var error = Assert.Throws<SaveFormatException>(() =>
            AttributeDecoder.Validate(new ResourceAttribute("Counter", LarianAttributeType.Int, new byte[3])));
        Assert.Equal("Invalid size for LSOF Int attribute.", error.Message);
    }

    [Fact]
    public void UnsupportedAttributeRetainsItsNumericDiagnostic()
    {
        var names = new Dictionary<uint, string> { [0] = "Counter" };
        var nodes = new byte[12];
        BinaryPrimitives.WriteInt32LittleEndian(nodes.AsSpan(8), -1);
        var attributes = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(attributes.AsSpan(4), 63);
        var error = Assert.Throws<SaveFormatException>(() => LsfTables.Read(names, nodes, attributes, [], [], false));
        Assert.Equal("Unsupported LSOF attribute type 63.", error.Message);
    }
}
