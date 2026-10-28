using System.Text;
using SaveToolkit.Sdk;

namespace SaveToolkit.Plugins.Cyberpunk2077.Tests;

public sealed class GarageFormatTests
{
    [Fact]
    public void IndependentlyEncodedActiveVehicleListUsesTypedHashesAndNestedTerminators()
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.ASCII, true);
        writer.Write(0x519E6795054B2C32UL); // FNV64 activeVehicles.
        writer.Write(0x08DE5A9D4EEE7776UL); // FNV64 static:3,vehicleGarageVehicleID.
        writer.Write(3);
        writer.Write(0x654BC9B034382DA1UL); // recordID.
        writer.Write(0x4072151FF3DCF7BCUL); // TweakDBID.
        writer.Write(0x0000002486F51E9CUL);
        writer.Write(0UL); // First nested vehicle end.
        writer.Write(0UL); // Empty second.
        writer.Write(0UL); // Empty third.
        writer.Flush();
        var body = output.ToArray();
        var vehicle = Assert.Single(GarageReader.ReadBody(body));
        Assert.Equal(0x0000002486F51E9CUL, vehicle.Record);
        Assert.Equal("activeVehicles", vehicle.List);
        for (var length = 1; length < body.Length; length++)
            Assert.Throws<SaveFormatException>(() => GarageReader.ReadBody(body.AsSpan(0, length)));
        body[8] ^= 1;
        Assert.Throws<SaveFormatException>(() => GarageReader.ReadBody(body));
    }

    [Fact]
    public void UnknownPropertiesAndDuplicatePropertiesNeverProduceOwnershipClaims()
    {
        Assert.Empty(GarageReader.ReadBody([]));
        byte[] body = [0x32, 0x2C, 0x4B, 0x05, 0x95, 0x67, 0x9E, 0x51,
            0x76, 0x77, 0xEE, 0x4E, 0x9D, 0x5A, 0xDE, 0x08,
            3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Empty(GarageReader.ReadBody(body));
        Assert.Throws<SaveFormatException>(() => GarageReader.ReadBody([.. body, .. body]));
        body[0] ^= 1;
        Assert.Throws<SaveFormatException>(() => GarageReader.ReadBody(body));
    }
}
