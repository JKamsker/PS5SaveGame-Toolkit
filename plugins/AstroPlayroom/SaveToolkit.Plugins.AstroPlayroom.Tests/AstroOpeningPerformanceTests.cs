using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Plugins.AstroPlayroom.Tests;

public sealed class AstroOpeningPerformanceTests
{
    [Fact]
    public void WideOpenDoesNotBuildDiscardedPresentationMetadata()
    {
        var text = new StringBuilder("\r\n<PreInstall><Unknown");
        for (var i = 0; i < 2048; i++) text.Append($" a{i}='v{i}'");
        text.Append("/></PreInstall>");
        var file = AstroTestData.File(AstroTestData.Bytes(text.ToString()));
        var plugin = new AstroPlayroomPlugin();
        var allocated = AstroPerformanceData.Allocated(() =>
        {
            using var document = plugin.Open(file);
        });
        Assert.InRange(allocated, 0, 3_400_000);
    }

    [Fact]
    public void LargestSampleOpenAndFirstViewStayWithinAllocationBudget()
    {
        var (plugin, _, context) = AstroCatalogueTestData.References();
        var file = AstroTestData.Samples().OrderByDescending(file => file.Length)
            .ThenByDescending(file => file.Directory.Generation).ThenBy(file => file.Directory.IsBackup).First();
        Assert.Equal(AstroTestData.AllocationSize, file.Length);
        var allocated = AstroPerformanceData.Allocated(() =>
        {
            using var document = plugin.Open(file, context);
            _ = document.BuildView();
        });
        Assert.InRange(allocated, 0, 5_200_000);
    }

    [Fact]
    public void MaximumMixedTextRetainsEveryReadOnlyValueWithoutRereadingTheFile()
    {
        const int count = 4096;
        var bytes = AstroTestData.Bytes("\r\n<PreInstall><Common><NumCoin>" +
            string.Concat(Enumerable.Repeat("1<!--gap-->", count)) + "</NumCoin></Common></PreInstall>");
        var file = new CountingFile(AstroTestData.File(bytes));
        using var document = new AstroPlayroomPlugin().Open(file);
        var fields = AstroTestData.Fields(document).Where(field => field.ValueId.StartsWith('/')).ToArray();
        Assert.Equal(count, fields.Length);
        Assert.All(fields, field =>
        {
            Assert.True(field.Spec.ReadOnly);
            Assert.Equal(Confidence.Unknown, field.Confidence);
            Assert.Equal(FieldValue.Of(1L), document.GetValue(field.ValueId));
        });
        Assert.Equal(Enumerable.Range(0, count).Select(i => $"/PreInstall[0]/Common[0]/NumCoin[0]/text[{i}]"),
            fields.Select(field => field.ValueId));
        Assert.Empty(Assert.IsAssignableFrom<IQueryableDocument>(document).Query("astro.collectibles", new()).Rows);
        Assert.NotEmpty(Assert.IsAssignableFrom<IActionPlanningDocument>(document)
            .PlanAction("common/set-coins", new Dictionary<string, FieldValue> { ["amount"] = FieldValue.Of(2L) }, new()).Errors);
        Assert.Equal(bytes, AstroTestData.Output(document));
        Assert.Equal(1, file.Reads);
    }

    private sealed class CountingFile(ISaveFile inner) : ISaveFile
    {
        public int Reads { get; private set; }
        public string Id => inner.Id;
        public ISaveDirectory Directory => inner.Directory;
        public string RelativePath => inner.RelativePath;
        public string PathInDirectory => inner.PathInDirectory;
        public string FileName => inner.FileName;
        public long Length => inner.Length;
        public byte[] ReadHeader(int count) => inner.ReadHeader(count);
        public Stream OpenRead()
        {
            Reads++;
            return inner.OpenRead();
        }
        public byte[] ReadAllBytes()
        {
            Reads++;
            return inner.ReadAllBytes();
        }
    }
}
