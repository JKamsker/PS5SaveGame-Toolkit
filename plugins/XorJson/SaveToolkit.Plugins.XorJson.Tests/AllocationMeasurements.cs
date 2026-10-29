namespace SaveToolkit.Plugins.XorJson.Tests;

// The SDK's shared numeric regex caches a runner. Concurrent parser tests can
// force extra runner allocations onto the measured thread, unrelated to this plugin.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AllocationMeasurements
{
    public const string Name = "XorJson allocation measurements";
}
