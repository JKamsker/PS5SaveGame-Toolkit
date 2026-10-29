using System.Security.Cryptography;
using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests;

public sealed class SaveMutationTests
{
    [Fact]
    public void Mutations_AreIndependentDeterministicAndLeaveTheSeedUntouched()
    {
        var original = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var untouched = original.ToArray();
        var cases = Enumerable.Range(0, 200).Select(i => SaveMutation.Create(original, SaveMutation.DefaultSeed, i)).ToArray();
        foreach (var index in Enumerable.Range(0, 200).Reverse())
        {
            var again = SaveMutation.Create(original, SaveMutation.DefaultSeed, index);
            Assert.Equal(cases[index].Content, again.Content);
            Assert.Equal(cases[index].Description, again.Description);
            Assert.InRange(again.Content.Length, 0, original.Length + 256);
        }

        Assert.Equal(untouched, original);
        Assert.NotEqual(cases[0].Content, SaveMutation.Create(original, SaveMutation.DefaultSeed + 1, 0).Content);
        Assert.StartsWith("bit flip", cases[0].Description);
        Assert.StartsWith("insert", cases[1].Description);
        Assert.StartsWith("delete", cases[2].Description);
        Assert.StartsWith("truncate", cases[3].Description);
        Assert.StartsWith("overwrite", cases[4].Description);
        Assert.StartsWith("duplicate", cases[5].Description);
    }

    [Fact]
    public void Sequence_HasAStableCrossRuntimeGoldenVector()
    {
        var input = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        for (var i = 0; i < 200; i++) hash.AppendData(SaveMutation.Create(input, SaveMutation.DefaultSeed, i).Content);
        Assert.Equal("3AC870DFE496D65EDAF14EE48974557B90D932DBC9DD18C237DDF736207EAB5F", Convert.ToHexString(hash.GetHashAndReset()));
    }

    [Theory]
    [InlineData(4, "0000")]
    [InlineData(10, "FFFF")]
    [InlineData(16, "0040")]
    [InlineData(34, "00000040")]
    [InlineData(52, "0000000000000040")]
    [InlineData(70, "4000")]
    [InlineData(88, "40000000")]
    [InlineData(106, "4000000000000000")]
    public void LengthOverwrites_ExerciseZeroOnesAndHugeValuesInBothByteOrders(int index, string hex)
    {
        var original = Enumerable.Repeat((byte)0xAA, 256).ToArray();
        var mutation = SaveMutation.Create(original, SaveMutation.DefaultSeed, index);
        Assert.Equal(Convert.FromHexString(hex), mutation.Content.Where(b => b != 0xAA).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    public void TinySeeds_AreSafeForEveryMutation(int length)
    {
        for (var i = 0; i < 200; i++)
            Assert.InRange(SaveMutation.Create(new byte[length], SaveMutation.DefaultSeed, i).Content.Length, 0, length + 256);
    }
}
