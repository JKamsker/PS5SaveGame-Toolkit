using System.Security.Cryptography;
using System.Text.Json;

namespace SaveToolkit.Testing.Ff7Browser;

internal static class FixtureProgram
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static void Main(string[] args)
    {
        if (args.Length != 1 || !Path.IsPathFullyQualified(args[0]))
            throw new ArgumentException("Supply one absolute, process-owned scratch output directory.");
        Directory.CreateDirectory(args[0]);
        var remake = Ff7Binary.Create(Ff7Actions.Titles[0]);
        var rebirth = Ff7Binary.Create(Ff7Actions.Titles[1]);
        var variants = new Dictionary<string, Ff7Binary>(StringComparer.Ordinal)
        {
            ["remake"] = remake,
            ["rebirth"] = rebirth,
            ["demo"] = Ff7Binary.Create(Ff7Actions.Titles[2]),
            ["remake-unique"] = remake.InventoryMatches(0),
            ["remake-ambiguous"] = remake.InventoryMatches(1, 7),
            ["remake-missing"] = remake.InventoryMatches(),
            ["rebirth-moved"] = rebirth.OwnedVariant("moved"),
            ["rebirth-duplicate-detail"] = rebirth.OwnedVariant("duplicate-detail"),
        };
        foreach (var (name, binary) in variants)
        {
            File.WriteAllBytes(Path.Combine(args[0], name + ".zip"), binary.Zip());
            var metadata = new
            {
                binary.TitleId, binary.GameplayName, binary.CommonName, binary.GameplayPath, binary.CommonPath,
                binary.ItemType, binary.MateriaType, binary.SecondTierAp, binary.FieldOffsets,
                proof = "Authored linked framing through production parser; no game checksum or native console proof.",
                members = new[]
                {
                    new { path = binary.GameplayPath, length = binary.Gameplay.Length, sha256 = Convert.ToHexString(SHA256.HashData(binary.Gameplay)) },
                    new { path = binary.CommonPath, length = binary.Common.Length, sha256 = Convert.ToHexString(SHA256.HashData(binary.Common)) },
                },
            };
            File.WriteAllText(Path.Combine(args[0], name + ".json"), JsonSerializer.Serialize(metadata, Json));
        }
    }
}
