using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using SaveToolkit.Core;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

/// <summary>
/// <c>GET …/changes</c> after one edit of the authored BL3/Wonderlands action-effect saves (the
/// browser repro fixture). The review used to take 40 s natively and 190 s in WebAssembly, beyond the
/// UI's 60 s request timeout, because each field the walk registered rebuilt the whole metadata cache.
/// </summary>
[Collection(typeof(AllocationTestCollection))]
public sealed class ChangesReviewPerformanceTests(ITestOutputHelper output)
{
    // Allocations are deterministic, unlike wall time. The quadratic review allocated 4.3 GB (BL3)
    // and 1.5 GB (Wonderlands); the fixed one allocates 0.27 GB and 0.15 GB.
    private const long AllocationLimit = 1L * 1024 * 1024 * 1024;

    // LocalApi's handler JSON (ResponseWriter.HttpJson): the exact response body bytes.
    private static readonly JsonSerializerOptions HttpJson = ToolkitJson.Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });

    /// <summary>
    /// The expected digests were recorded from the response bodies before the metadata-cache fix,
    /// which must not change a byte of the review.
    /// </summary>
    [Theory]
    [InlineData(false, "4a3ea8fa587eaa245c2342e8f7697500ead6dfafa457b83e2c4638c11fc4e17f")]
    [InlineData(true, "e1d0c27db8705a09f7ce72602447de9b357953cd393067236dc8a1574cf90654")]
    public void ChangesAfterOneEditKeepTheirBodyWithBoundedAllocations(bool wonderlands, string expectedSha256)
    {
        using var culture = new TestCulture(CultureInfo.InvariantCulture);
        using var workspace = MetadataR6Data.Workspace(ActionEffectData.Set(wonderlands));
        var file = workspace.Set.AllDiskFiles.Single(file => file.FileName == "1");
        var session = workspace.Open(file.Id, "gearbox");
        _ = session.BuildView();
        var edit = session.ApplyEdits([new IncomingEdit("character/xp", JsonSerializer.SerializeToElement(1))], null,
            TestContext.Current.CancellationToken);
        Assert.True(edit.Ok, string.Join("; ", edit.Errors));

        var started = Stopwatch.GetTimestamp();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var log = session.GetChanges(TestContext.Current.CancellationToken);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        var elapsed = Stopwatch.GetElapsedTime(started);
        var body = JsonSerializer.SerializeToUtf8Bytes<object?>(log, HttpJson);
        var digest = Convert.ToHexStringLower(SHA256.HashData(body));
        output.WriteLine($"{(wonderlands ? "Wonderlands" : "BL3")}: {allocated} B allocated; {elapsed.TotalMilliseconds:F0} ms (diagnostic only); body {body.Length} B sha256 {digest}");

        Assert.Equal(expectedSha256, digest);
        Assert.True(allocated < AllocationLimit, $"One review allocated {allocated} B (limit {AllocationLimit} B).");
    }
}
