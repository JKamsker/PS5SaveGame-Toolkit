using SaveToolkit.Codecs.Oodle;

namespace SaveToolkit.Codecs.Tests;

public sealed class OodleBufferBoundsTests
{
    [Fact]
    public void View_extent_survives_casts_and_cursor_arithmetic_in_oversized_rentals()
    {
        const int logicalLength = 65;
        byte[] rental = new byte[1024];
        var budget = new DecodeBudget(1000);
        var view = new BufferView<byte>(rental, 0, budget, logicalLength);
        (view + logicalLength - 1).Value = 42;
        Assert.Equal(42, rental[logicalLength - 1]);
        Assert.Throws<ArgumentOutOfRangeException>(() => (view + logicalLength).Value = 42);
        Assert.Throws<ArgumentOutOfRangeException>(() => (view + logicalLength - 1).As<uint>().Value = 42);
        Assert.Throws<ArgumentOutOfRangeException>(() => (view + logicalLength - 1).Bytes(2).Clear());
        Assert.Throws<ArgumentOutOfRangeException>(() => (view - 1).Value = 42);
        Assert.All(rental[logicalLength..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void Workspace_rejects_reentrancy_and_disposal_during_decode()
    {
        using var workspace = new OodleWorkspace();
        workspace.Acquire();
        Assert.Throws<InvalidOperationException>(workspace.Acquire);
        Assert.Throws<InvalidOperationException>(workspace.Dispose);
        workspace.Release();
        OodleCodec.Decompress([], Span<byte>.Empty, workspace);
    }

    [Fact]
    public void Reused_buffers_are_zeroed_and_each_view_obeys_its_new_budget()
    {
        using var workspace = new OodleWorkspace();
        workspace.Source(1024).AsSpan().Fill(0xA5);
        workspace.Destination(1024).AsSpan().Fill(0xA5);
        workspace.Scratch(1024, new DecodeBudget(4096)).Bytes(1024).Fill(0xA5);
        Assert.All(workspace.Source(128)[..128], value => Assert.Equal(0, value));
        Assert.All(workspace.Destination(128)[..128], value => Assert.Equal(0, value));
        var scratch = workspace.Scratch(128, new DecodeBudget(129));
        Assert.All(scratch.Bytes(128).ToArray(), value => Assert.Equal(0, value));
        _ = scratch.Value;
        Assert.Throws<InvalidDataException>(() => scratch.Value = 42);
        Assert.Throws<ArgumentOutOfRangeException>(() => workspace.Scratch(128, new DecodeBudget(1000)).Bytes(129).Clear());
    }
}
