using K4os.Compression.LZ4.Encoders;
using SaveToolkit.Sdk.Compression;

namespace SaveToolkit.Plugins.BaldursGate3;

/// <summary>
/// K4os <c>LZ4FastChainEncoder</c>/<c>LZ4HighChainEncoder</c> (input ring buffer of <c>LZ4EncoderBase</c>)
/// driving one of the SDK's managed linked-block engines, so <c>LZ4EncoderStream</c> writes the frames
/// the native host writes on every platform: K4os' HC state is mis-addressed by the browser runtime's
/// interpreter (see <see cref="Lz4HighCompression"/>), and its fast engine is the 32-bit variant there
/// (see <see cref="Lz4FastCompression"/>).
/// </summary>
/// <remarks>
/// Unsafe only because <see cref="ILZ4Encoder"/> exchanges raw pointers with the K4os frame writer; each
/// pointer is wrapped in a span of the length K4os passes with it and never stored.
/// </remarks>
internal sealed unsafe class ManagedLz4ChainEncoder : ILZ4Encoder
{
    private const int DictionarySize = 64 * 1024;
    private readonly ILz4CompressionChain _chain;
    private readonly byte[] _input;
    private readonly int _inputLength;
    private int _inputIndex;
    private int _inputPointer;

    public ManagedLz4ChainEncoder(ILz4CompressionChain chain, int blockSize, int extraBlocks)
    {
        blockSize = (Math.Max(blockSize, 1024) + 1023) / 1024 * 1024;
        BlockSize = blockSize;
        _inputLength = DictionarySize + (1 + Math.Max(extraBlocks, 0)) * blockSize + 32;
        _input = new byte[_inputLength + 8];
        _chain = chain;
    }

    public int BlockSize { get; }

    public int BytesReady => _inputPointer - _inputIndex;

    public int Topup(byte* source, int length)
    {
        if (length == 0) return 0;
        var spare = _inputIndex + BlockSize - _inputPointer;
        if (spare <= 0) return 0;
        var chunk = Math.Min(spare, length);
        new ReadOnlySpan<byte>(source, chunk).CopyTo(_input.AsSpan(_inputPointer));
        _inputPointer += chunk;
        return chunk;
    }

    public int Encode(byte* target, int length, bool allowCopy)
    {
        var sourceLength = _inputPointer - _inputIndex;
        if (sourceLength <= 0) return 0;
        var output = new Span<byte>(target, length);
        var encoded = _chain.Compress(_input, _inputIndex, sourceLength, output);
        if (encoded <= 0) throw new InvalidOperationException("Failed to encode chunk. Target buffer too small.");
        if (allowCopy && encoded >= sourceLength)
        {
            _input.AsSpan(_inputIndex, sourceLength).CopyTo(output);
            encoded = -sourceLength;
        }

        _inputIndex = _inputPointer;
        if (_inputIndex + BlockSize > _inputLength)
            _inputIndex = _inputPointer = _chain.SaveDictionary(_input, _inputPointer);
        return encoded;
    }

    public void Dispose()
    {
    }
}
