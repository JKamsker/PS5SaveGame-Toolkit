namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>
/// ZLibStream treats underlying EOF as success even when its member is unfinished.
/// A complete member stops requesting input after validating its trailer. Reject an
/// additional input request at EOF so truncated members cannot masquerade as complete.
/// This is local to BG3 decoding; it does not change .NET's global validation switch.
/// </summary>
internal sealed class ZlibInputStream(byte[] bytes) : MemoryStream(bytes, false)
{
    public override int Read(byte[] buffer, int offset, int count)
    {
        RequireInput(count);
        return base.Read(buffer, offset, count);
    }

    public override int Read(Span<byte> buffer)
    {
        RequireInput(buffer.Length);
        return base.Read(buffer);
    }

    private void RequireInput(int count) =>
        BinaryData.Require(count == 0 || Position < Length, "Truncated BG3 zlib member or missing checksum trailer.");
}
