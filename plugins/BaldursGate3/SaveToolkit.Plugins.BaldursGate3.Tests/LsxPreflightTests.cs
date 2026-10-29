using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class LsxPreflightTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExcessiveCountsFailBeforeMaterializingTheXmlTree(bool attributes)
    {
        var content = attributes ? string.Concat(Enumerable.Repeat("<n a='1' b='2'/>", BinaryData.MaxRecords / 2))
            : string.Concat(Enumerable.Repeat("<n/>", BinaryData.MaxRecords));
        var xml = Encoding.UTF8.GetBytes("<save id='root'>" + content + "</save>");
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var error = Record.Exception(() => LsxReader.Read(xml));
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.IsType<SaveFormatException>(error);
        Assert.True(allocated < 1024 * 1024, $"Excessive XML counts allocated {allocated:N0} bytes before rejection.");
    }

    [Fact]
    public void AttributeAndTextCountsAreChargedExactlyOnceAcrossResources()
    {
        var budget = new ResourceBudget();
        var bytes = Encoding.UTF8.GetBytes("<save id='a'> before <n id='b'>text<![CDATA[ more]]></n> after <empty/> </save>");
        var resource = LsxReader.Read(bytes, budget);
        Assert.Equal(3, resource.Nodes.Count);
        Assert.Equal(4, resource.Attributes.Count);
        budget.Reserve(0, BinaryData.MaxRecords - 3, BinaryData.MaxRecords - 4);
        Assert.Throws<SaveFormatException>(() => LsxReader.Read("<save/>"u8.ToArray(), budget));
    }
}
