using SaveToolkit.Testing;
using Xunit.Sdk;

namespace SaveToolkit.Core.Tests;

public sealed class MutationFuzzProcessTests
{
    [Fact]
    public async Task MutationFuzz()
    {
        if (Environment.GetEnvironmentVariable(MutationFuzzProcess.WorkerVariable) is { Length: > 0 } worker)
        {
            MutationFuzzProcess.Progress(worker, "case seed=0x40C0FFEE index=7 synthetic hung parser");
            Thread.Sleep(Timeout.Infinite);
            return;
        }

        var exception = await Assert.ThrowsAsync<XunitException>(() =>
            MutationFuzzProcess.RunAsync(GetType(), TimeSpan.FromMilliseconds(300)));
        Assert.Contains("index=7", exception.Message);
        Assert.Contains("exceeded", exception.Message);
    }
}
