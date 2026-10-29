namespace SaveToolkit.Plugins.EldenRing;

// ZLibStream may treat source EOF as success even before its final block/checksum.
// A complete member finishes internally without another source read. If the inflater
// needs bytes after this bounded DCX payload, the member is necessarily truncated.
internal sealed class RegulationInputStream(byte[] bytes) : MemoryStream(bytes, writable: false)
{
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = base.Read(buffer, offset, count);
        RequireInput(read, count);
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var read = base.Read(buffer);
        RequireInput(read, buffer.Length);
        return read;
    }

    private static void RequireInput(int read, int requested)
    {
        if (requested > 0 && read == 0)
            throw new EndOfStreamException("Regulation zlib member requires data beyond its DCX payload.");
    }
}
