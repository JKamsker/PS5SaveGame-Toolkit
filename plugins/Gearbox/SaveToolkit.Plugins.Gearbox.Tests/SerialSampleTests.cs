using SaveToolkit.Testing;
using SaveToolkit.Sdk.Values;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class SerialSampleTests
{
    [Theory]
    [InlineData("PPSA01463", false, 93, 83, 85, 4, 62)]
    [InlineData("PPSA01493", true, 68, 51, 51, 5, 59)]
    public void IndependentProbes_ReproduceEveryPrimaryOccurrenceAndPreserveBackups(string title, bool wonderlands,
        int expectedCount, int expectedNames, int expectedParts, int serialVersion, int databaseVersion)
    {
        var files = SampleSaves.Load(title).AllDiskFiles.Where(f => new GearboxPlugin().Probe(f).IsMatch).ToArray();
        var database = SerialDatabase.For(wonderlands);
        var reader = new SerialReader(wonderlands);
        var count = 0;
        var names = 0;
        var complete = 0;
        foreach (var file in files)
        {
            var bytes = file.ReadAllBytes();
            var profile = file.FileName == "profile";
            var independent = IndependentSaveProbe.Read(bytes, wonderlands, profile);
            var root = GearboxContainer.Parse(bytes, title, profile).Decode();
            var fields = profile ? root.All("bank_inventory_list").Concat(root.All("lost_loot_inventory_list")) : root.All("inventory_items");
            var actual = fields.Select(f => f.Message?.Last("item_serial_number")?.Value as byte[] ?? f.Value as byte[] ?? []).ToArray();
            Assert.Equal(independent.Serials.Count, actual.Length);
            for (var i = 0; i < actual.Length; i++)
            {
                Assert.Equal(independent.Serials[i], actual[i]);
                var envelope = SerialEnvelope.Decode(actual[i], wonderlands);
                Assert.Equal(IndependentSerialProbe.Decode(actual[i]), envelope.Payload.ToArray());
                Assert.Equal(actual[i], envelope.EncodeOriginalSeed());
                Assert.Equal(actual[i], IndependentSerialProbe.Encode(envelope.Payload.ToArray(), envelope.Version, envelope.Seed));
                var decoded = reader.Read(actual[i]);
                Assert.True(decoded.ChecksumValid, decoded.Diagnostic);
                Assert.Equal(serialVersion, decoded.Version);
                Assert.Equal(databaseVersion, decoded.DatabaseVersion);
                Assert.NotNull(decoded.Level);
                Assert.NotNull(decoded.Balance?.Path);
                Assert.NotNull(decoded.Inventory?.Path);
                Assert.NotNull(decoded.Manufacturer?.Path);
                var bits = new ProbeBits(envelope.Payload.ToArray());
                Assert.Equal(128, bits.Take(8));
                Assert.Equal(databaseVersion, bits.Take(7));
                Assert.Equal(decoded.Balance.Index, bits.Take(database.Width("InventoryBalanceData", databaseVersion)));
                Assert.Equal(decoded.Inventory.Index, bits.Take(database.Width("InventoryData", databaseVersion)));
                Assert.Equal(decoded.Manufacturer.Index, bits.Take(database.Width("ManufacturerData", databaseVersion)));
                Assert.Equal(decoded.Level, bits.Take(7));
                if (file.Directory.IsBackup) continue;
                count++;
                if (database.Find("balances", decoded.Balance.Path.ToLowerInvariant())?.Attributes["displayKind"] == "family") names++;
                if (decoded.Complete) complete++;
            }
            using var document = new GearboxPlugin().Open(file);
            Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
        }
        Assert.Equal(expectedCount, count);
        Assert.Equal(expectedNames, names);
        Assert.Equal(expectedParts, complete);
    }

    [Theory]
    [InlineData("PPSA01463", false)]
    [InlineData("PPSA01493", true)]
    public void CurrencyEdits_LeaveEverySerialAndUnrelatedContainerFieldUntouched(string title, bool wonderlands)
    {
        foreach (var file in SampleSaves.Load(title).AllDiskFiles.Where(f => f.FileName == "1" && !f.Directory.IsBackup))
        {
            var original = file.ReadAllBytes();
            var before = IndependentSaveProbe.Read(original, wonderlands, false);
            using var document = new GearboxPlugin().Open(file);
            _ = document.BuildView();
            var oldMoney = document.GetValue("currency/618814354");
            var oldXp = document.GetValue("character/xp");
            var money = oldMoney.AsInt64() + 1;
            Assert.True(document.ApplyEdits([new("currency/618814354", FieldValue.Of(money))]).Succeeded);
            var encoded = document.Serialize().Single().Content.ToArray();
            var after = IndependentSaveProbe.Read(encoded, wonderlands, false);
            Assert.Equal((ulong)money, after.Money);
            Assert.Equal(before.Experience, after.Experience);
            Assert.Equal(before.Serials.Count, after.Serials.Count);
            for (var i = 0; i < before.Serials.Count; i++) Assert.Equal(before.Serials[i], after.Serials[i]);
            var a = GearboxContainer.Parse(original, title, false).Decode();
            var b = GearboxContainer.Parse(encoded, title, false).Decode();
            var untouched = a.Fields.Where(f => f.Spec?.Name != "inventory_category_list").Select(f => f.Encode()).ToArray();
            var output = b.Fields.Where(f => f.Spec?.Name != "inventory_category_list").Select(f => f.Encode()).ToArray();
            Assert.Equal(untouched.Length, output.Length);
            for (var i = 0; i < untouched.Length; i++) Assert.Equal(untouched[i], output[i]);
            Assert.True(document.ApplyEdits([new("currency/618814354", oldMoney), new("character/xp", oldXp)]).Succeeded);
            Assert.Equal(original, document.Serialize().Single().Content.ToArray());
        }
    }
}
