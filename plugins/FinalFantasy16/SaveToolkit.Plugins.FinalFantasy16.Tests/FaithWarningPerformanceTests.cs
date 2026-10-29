namespace SaveToolkit.Plugins.FinalFantasy16.Tests;

public sealed class FaithWarningPerformanceTests
{
    [Theory]
    [InlineData(1, "unmapped")]
    [InlineData(2, "unmapped")]
    [InlineData(3, "unmapped")]
    [InlineData(5, "consumable")]
    [InlineData(100, "quest-item")]
    [InlineData(100, "curiosity")]
    [InlineData(100, "orchestrion")]
    [InlineData(100, "weapon")]
    [InlineData(100, "belt")]
    [InlineData(100, "vambrace")]
    [InlineData(100, "accessory")]
    [InlineData(60000, "unmapped")]
    public void RepeatedPolicyWarningsDoNotAllocatePerRow(int id, string category)
    {
        var warning = FaithStackPolicy.Warning(id, category);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var repeat = 0; repeat < 100; repeat++) GC.KeepAlive(FaithStackPolicy.Warning(id, category));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 1024, $"Repeated fixed policy warnings allocated {allocated:N0} bytes.");
        Assert.Contains("Console load/save/reload is untested", warning);
        Assert.Contains("not a gameplay cap", warning);
    }
}
