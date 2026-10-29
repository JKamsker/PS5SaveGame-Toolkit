using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SaveToolkit.Sdk;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Witcher3.Tests;

public sealed class WitcherDiagnosticCharacterizationTests
{
    [Theory]
    [InlineData(false, "93B8C0606ED6F42F208963B3F914BDF03B8C2C02734ABA67601C1AD8CFACAF45")]
    [InlineData(true, "00091082652CF86940BB00D46643BC4467F9F09B383C4A4D479A5BCFC92CA662")]
    public void SyntheticCorruptionKeepsExactDiagnosticsAndAcceptance(bool player, string expectedHash)
    {
        var original = player ? SyntheticPlayerSave.Create() : SyntheticSave.Create();
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            // SavedString includes the runtime's decoder diagnostic; pin its language for cross-platform goldens.
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            var results = new List<string>();
            Corrupt(original, results, bytes => bytes);
            Corrupt(WitcherContainer.Parse(original).Payload, results, SyntheticSave.Wrap);
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', results))));
            Assert.Equal(expectedHash, hash);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }

    private static void Corrupt(byte[] original, List<string> results, Func<byte[], byte[]> wrap)
    {
        for (var length = 0; length <= original.Length; length++) { results.Add(Observe(wrap(original[..length]))); }
        var random = new Random(0x53415633);
        const int mutations = 512;
        for (var index = 0; index < mutations; index++)
        {
            var bytes = original.ToArray();
            bytes[random.Next(bytes.Length)] ^= (byte)random.Next(1, 256);
            results.Add(Observe(wrap(bytes)));
        }

    }

    private static string Observe(byte[] bytes)
    {
        try
        {
            using var document = new Witcher3Plugin().Open(MemorySaveSet.SingleFile("Manual.sav", bytes, "PPSA04018"));
            _ = document.BuildView();
            Assert.Equal(bytes, Assert.Single(document.Serialize()).Content.ToArray());
            return $"accepted:{document.CanEdit}";
        }
        catch (SaveFormatException error)
        {
            return error.Message;
        }
    }
}
