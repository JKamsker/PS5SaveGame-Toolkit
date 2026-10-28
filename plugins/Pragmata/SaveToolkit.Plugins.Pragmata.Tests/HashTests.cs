using System.Text;
using SaveToolkit.Plugins.Pragmata.Crypto;
using SaveToolkit.Plugins.Pragmata.Format;
using SaveToolkit.Plugins.Pragmata.Hashing;

namespace SaveToolkit.Plugins.Pragmata.Tests;

public sealed class HashTests
{
    // Expected values from the independently verified research Python toolkit.
    // Input pattern covers byte-order, all short-length branches and the 64-byte loop boundaries.
    [Theory]
    [InlineData(0, 0x9ae16a3b2f90404fUL, 0x81f16f39U)]
    [InlineData(1, 0x4271b80e2915c76aUL, 0x0356425bU)]
    [InlineData(3, 0x373a2b2a12e0c581UL, 0x9e95b6a1U)]
    [InlineData(4, 0xcc1e778137ae6d69UL, 0xde692f70U)]
    [InlineData(7, 0x960838fbc6d9c975UL, 0x5f39ccf9U)]
    [InlineData(8, 0x7d507025ed4eecfdUL, 0x828dc9acU)]
    [InlineData(16, 0xd836480326a974d6UL, 0x15a4ed7fU)]
    [InlineData(17, 0x5cded6cb65c9dac6UL, 0xe761a468U)]
    [InlineData(31, 0x52a467e87c92d522UL, 0xbe668443U)]
    [InlineData(32, 0xa03300e1b2be6da7UL, 0x2da72643U)]
    [InlineData(33, 0x6e14215d4c7200d4UL, 0x44721047U)]
    [InlineData(63, 0xc9fc77dead95c3aaUL, 0x03029d81U)]
    [InlineData(64, 0x2a7ab541551d3235UL, 0xdd29b6fdU)]
    [InlineData(65, 0x5709ecef010a51e4UL, 0x5dc937b1U)]
    [InlineData(127, 0xecd6ea5dfa196e8cUL, 0x6c7522b5U)]
    [InlineData(128, 0xa9e79ad8f1c9f5a7UL, 0x97aae5abU)]
    [InlineData(129, 0x6bb7bad48ee8ebe7UL, 0x81c1d9f0U)]
    [InlineData(1024, 0x38aed3306af49be7UL, 0x869a05cbU)]
    public void ResearchVectors(int length, ulong city, uint murmur)
    {
        var bytes = Enumerable.Range(0, length).Select(i => (byte)(i * 37 + 11)).ToArray();
        Assert.Equal(city, CityHash64.Hash(bytes));
        Assert.Equal(murmur, Murmur3.Hash(bytes));
    }

    [Fact]
    public void EveryEmbeddedNameReproducesItsHash()
    {
        Assert.Equal(413, HashNames.All.Count);
        Assert.All(HashNames.All, pair => Assert.Equal(pair.Key, Murmur3.Hash(Encoding.UTF8.GetBytes(pair.Value))));
        Assert.Equal("_SaveSlotIconHash", HashNames.All[0x6182c62b]);
        Assert.Equal("_SavePlayActualTime", HashNames.All[KnownHashes.Playtime]);
    }

    [Fact]
    public void AesOfbMatchesNistSp80038AVectorIncludingPartialBlock()
    {
        var key = Convert.FromHexString("2b7e151628aed2a6abf7158809cf4f3c");
        var iv = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
        var plaintext = Convert.FromHexString("6bc1bee22e409f96e93d7e117393172aae2d8a571e03ac9c9eb76fac45af8e5130c81c46a35ce411e5fbc1191a0a52eff69f2445df4f9b17ad2b417be66c3710");
        var expected = Convert.FromHexString("3b3fd92eb72dad20333449f8e83cfb4a7789508d16918f03f53c52dac54ed8259740051e9c5fecf64344f7a82260edcc304c6528f659c77866a510d9c1d6ae5e");
        foreach (var length in new[] { 1, 15, 16, 17, 63, 64 })
        {
            var actual = new byte[length];
            AesOfb.Transform(plaintext.AsSpan(0, length), actual, key, iv);
            Assert.Equal(expected[..length], actual);
            AesOfb.Transform(actual, actual, key, iv);
            Assert.Equal(plaintext[..length], actual);
        }
    }
}
