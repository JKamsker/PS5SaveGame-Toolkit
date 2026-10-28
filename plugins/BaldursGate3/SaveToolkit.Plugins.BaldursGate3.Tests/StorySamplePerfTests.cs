using System.Diagnostics;

namespace SaveToolkit.Plugins.BaldursGate3.Tests;

public sealed class StorySamplePerfTests
{
    [Fact]
    public void SampleStoryMarkerFirstExpansionStaysBounded()
    {
        using var document = new BaldursGate3Plugin().Open(SampleLiveNewAgeTests.Save());
        var timer = Stopwatch.StartNew();
        _ = document.ExpandNode("story/databases");
        timer.Stop();
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(1), $"Story marker expansion took {timer.Elapsed}.");
    }
}
