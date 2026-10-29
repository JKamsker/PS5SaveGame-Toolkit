using System.Text.Json;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Editing;

/// <summary>Measures actual normalized UTF-8, including full specs, annotations, escaping and repeated values.</summary>
internal static class FieldReferenceWireBudget
{
    internal const int MaximumBytes = 2 * 1024 * 1024;
    private const string LimitMessage = "The resolved field-reference view exceeds the 2 MiB response size limit.";

    public static void CheckField(FieldNode field, CancellationToken cancellationToken)
    {
        CheckText(field.Label);
        CheckText(field.Warning);
        CheckText(field.Description);
        var value = field.Value;
        while (value.Kind == FieldValueKind.Union) value = value.AsUnion().Value;
        if (value.Kind == FieldValueKind.Text) CheckText(value.AsString());
        Check(field, cancellationToken);
    }

    private static void CheckText(string? text)
    {
        // Even unescaped ASCII cannot fit. Reject giant tokens before the serializer encodes them.
        if (text is { Length: > MaximumBytes }) throw new FieldReferenceResponseLimitException(LimitMessage);
    }

    public static void Check(ViewNode view, CancellationToken cancellationToken)
    {
        using var counter = new Counter(cancellationToken);
        JsonSerializer.Serialize(counter, view, ToolkitJson.Options);
    }

    private sealed class Counter(CancellationToken cancellationToken) : Stream
    {
        private long _bytes;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _bytes;
        public override long Position { get => _bytes; set => throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (buffer.Length > MaximumBytes - _bytes) throw new FieldReferenceResponseLimitException(LimitMessage);
            _bytes += buffer.Length;
        }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
