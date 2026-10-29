using System.Buffers.Binary;
using System.Text;

namespace SaveToolkit.Plugins.SuckerPunch;

internal sealed record TsushimaProgress(string Difficulty, float? PlaySeconds, int Start, int End)
{
    private const int PlayTimeRelativeOffset = 231;
    private const int DifficultyLookBehind = 80;
    public int PlayTimeOffset => Start + DifficultyLookBehind + PlayTimeRelativeOffset;

    public static TsushimaProgress? Parse(ReadOnlySpan<byte> content, uint version)
    {
        // Each name is length-prefixed and followed by a float; all four names must match in order.
        var searchStart = 0;
        TsushimaProgress? result = null;
        ReadOnlySpan<byte> first = "\x04\0\0\0Easy"u8;
        while (searchStart < content.Length && content[searchStart..].IndexOf(first) is var relative && relative >= 0)
        {
            var start = searchStart + relative;
            var valid = TryReadCounterTable(content, start, out var position);

            searchStart = start + first.Length;
            if (!valid || start < DifficultyLookBehind)
            {
                continue;
            }

            var difficulty = FindDifficulty(content.Slice(start - DifficultyLookBehind, DifficultyLookBehind));
            if (difficulty is null || result is not null)
            {
                return null;
            }

            var timerOffset = start + PlayTimeRelativeOffset;
            var seconds = ReadPlaySeconds(content, version, timerOffset);

            result = new TsushimaProgress(difficulty, seconds, start - DifficultyLookBehind,
                seconds.HasValue ? timerOffset + sizeof(float) : position);
        }

        return result;
    }

    private static bool TryReadCounterTable(ReadOnlySpan<byte> content, int start, out int position)
    {
        position = start;
        foreach (var name in new[] { "Easy", "Medium", "Hard", "Nightmare" })
        {
            if (position > content.Length - sizeof(uint) - name.Length - sizeof(float) ||
                BinaryPrimitives.ReadUInt32LittleEndian(content[position..]) != name.Length ||
                !content.Slice(position + sizeof(uint), name.Length).SequenceEqual(Encoding.ASCII.GetBytes(name)))
            {
                return false;
            }
            position += sizeof(uint) + name.Length + sizeof(float);
        }
        return true;
    }

    private static float? ReadPlaySeconds(ReadOnlySpan<byte> content, uint version, int timerOffset)
    {
        // v47 has a different tail layout: the same relative offset is NOT a play timer.
        if (version is 48 or 49 && timerOffset <= content.Length - sizeof(float))
        {
            var candidate = BinaryPrimitives.ReadSingleLittleEndian(content[timerOffset..]);
            if (float.IsFinite(candidate) && candidate >= 0)
            {
                return candidate;
            }
        }
        return null;
    }

    private static string? FindDifficulty(ReadOnlySpan<byte> prefix)
    {
        foreach (var name in new[] { "Easy", "Medium", "Hard", "Nightmare" })
        {
            var pattern = new byte[sizeof(uint) + name.Length + sizeof(uint) + "Ship".Length];
            BinaryPrimitives.WriteUInt32LittleEndian(pattern, (uint)name.Length);
            Encoding.ASCII.GetBytes(name, pattern.AsSpan(sizeof(uint)));
            var shipOffset = sizeof(uint) + name.Length;
            BinaryPrimitives.WriteUInt32LittleEndian(pattern.AsSpan(shipOffset), (uint)"Ship".Length);
            "Ship"u8.CopyTo(pattern.AsSpan(shipOffset + sizeof(uint)));
            if (prefix.IndexOf(pattern) >= 0)
            {
                return name;
            }
        }

        return null;
    }
}
