using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Witcher3;

/// <summary>Queries sorted, nonoverlapping patches without rescanning the edit batch.</summary>
internal sealed class Sav3PatchIndex
{
    private readonly Sav3Patch[] _patches;
    private readonly int[] _growth;

    public Sav3PatchIndex(Sav3Patch[] patches)
    {
        _patches = patches;
        _growth = new int[patches.Length + 1];
        for (var index = 0; index < patches.Length; index++)
            _growth[index + 1] = checked(_growth[index] + patches[index].Growth);
    }

    public int Relocate(int offset) => checked(offset + _growth[FirstEndingAfter(offset)]);

    public bool Replaces(int offset, int length)
    {
        var index = FirstEndingAfter(offset);
        return index < _patches.Length && _patches[index].Offset == offset && _patches[index].Length == length;
    }

    public int GrowthInside(int offset, int length)
    {
        var end = checked(offset + length);
        var first = FirstEndingAfter(offset);
        var last = FirstStartingAtOrAfter(end);
        if (first >= last) return 0;
        if (_patches[first].Offset < offset || _patches[last - 1].End > end)
            throw new SaveFormatException("SAV3 edit crosses a variable boundary.");
        return _growth[last] - _growth[first];
    }

    private int FirstEndingAfter(int offset)
    {
        var low = 0;
        var high = _patches.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_patches[middle].End <= offset) low = middle + 1;
            else high = middle;
        }
        return low;
    }

    private int FirstStartingAtOrAfter(int offset)
    {
        var low = 0;
        var high = _patches.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_patches[middle].Offset < offset) low = middle + 1;
            else high = middle;
        }
        return low;
    }
}
