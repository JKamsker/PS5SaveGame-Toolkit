namespace SaveToolkit.Codecs.Oodle;

// Each decode owns its budget; there is no thread-local or process-wide decoder state.
internal sealed class DecodeBudget(long operations)
{
    private long _remaining = operations;
    private int _depth;

    internal void Spend(long count = 1)
    {
        if (count < 0 || (_remaining -= count) < 0)
            throw new InvalidDataException("Kraken decoding exceeded the work limit.");
    }

    internal IDisposable Enter()
    {
        if (++_depth > 16)
            throw new InvalidDataException("Kraken entropy nesting exceeds 16 levels.");
        return new Scope(this);
    }

    private sealed class Scope(DecodeBudget budget) : IDisposable
    {
        public void Dispose() => budget._depth--;
    }
}
