using System.Diagnostics;
using System.Globalization;

namespace SaveToolkit.Api.Performance.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ApiMeasurementCollection
{
    public const string Name = "API allocation measurements";
}

internal sealed class ApiMeasurement(ITestOutputHelper output, string scenario)
{
    public async Task<T> RunAsync<T>(string operation, Func<Task<T>> run)
    {
        var before = GC.GetTotalAllocatedBytes(precise: true);
        var start = Stopwatch.GetTimestamp();
        var result = await run();
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        Record(operation, allocated, elapsed);
        return result;
    }

    public void Record(string operation, long allocated, double elapsed = 0)
    {
        var line = string.Create(CultureInfo.InvariantCulture, $"{scenario},{operation},{elapsed:F3},{allocated}");
        output.WriteLine(line);
        if (Environment.GetEnvironmentVariable("SAVETOOLKIT_API_MEASUREMENTS") is { Length: > 0 } path)
            File.AppendAllText(path, line + Environment.NewLine);
    }

    public void Describe(string message)
    {
        output.WriteLine(message);
        if (Environment.GetEnvironmentVariable("SAVETOOLKIT_API_MEASUREMENTS") is { Length: > 0 } path)
            File.AppendAllText(path + ".metadata.txt", $"{scenario}: {message}{Environment.NewLine}");
    }
}
