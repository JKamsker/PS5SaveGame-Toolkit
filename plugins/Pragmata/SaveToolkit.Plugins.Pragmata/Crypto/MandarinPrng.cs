namespace SaveToolkit.Plugins.Pragmata.Crypto;

internal sealed class MandarinPrng(ulong state)
{
    public ulong State { get; private set; } = state;

    public ulong Next()
    {
        unchecked
        {
            var value = State + 0x9e3779b97f4a7c15;
            value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9;
            value = (value ^ (value >> 27)) * 0x94d049bb133111eb;
            // Deliberately feed the mixed result back, unlike conventional SplitMix64.
            State = value ^ (value >> 31);
            return State;
        }
    }

    public void Add(ulong value) => State = unchecked(State + value);
}
