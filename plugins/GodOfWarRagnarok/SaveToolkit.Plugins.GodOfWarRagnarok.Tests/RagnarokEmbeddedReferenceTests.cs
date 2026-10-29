using System.Security.Cryptography;
using System.Text.Json;

namespace SaveToolkit.Plugins.GodOfWarRagnarok.Tests;

public sealed class RagnarokEmbeddedReferenceTests
{
    [Theory]
    // Captured from the private decoder before SDK-03 adoption, including record order and provenance.
    [InlineData("gow-ragnarok.ps5.en", 31, "2DE2F0D33FCDC81DD4ABA3DB1BDFFCD27667F379D5DB84B3A75C109307D9A7E2")]
    [InlineData("gow-ragnarok.community.en", 38, "3BB75A9852DA5B9B6A447D7D264DD96EC95F5043C4829AF933694166B2A26680")]
    [InlineData("gow-ragnarok.skills.en", 81, "B00412FF0C1C72BA75FF4F6A17D652934B2A69364CD0340975EC515A41B08312")]
    [InlineData("gow-ragnarok.runtime.en", 965, "3AC8977B53092F0959E7CCAE9077FF3DE5CB08DE20DE505B410320E8C0F7E94E")]
    [InlineData("gow-ragnarok.2018.en", 1682, "3F7362CDBE7B2B0287291687426BA48744F0B022211130A715E5A2326DE68A2F")]
    public void FallbackRetainsEveryRecordAndItsMetadata(string packId, int count, string expected)
    {
        var records = RagnarokReferenceLoader.Load(packId);
        Assert.Equal(count, records.Count);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(records.Values);
        var actual = Convert.ToHexString(SHA256.HashData(bytes));
        Assert.True(expected == actual, actual);
    }
}
