using SaveToolkit.Plugins.UnrealGvas.Gvas;
using SaveToolkit.Plugins.UnrealGvas.Hogwarts;

namespace SaveToolkit.Plugins.UnrealGvas;

internal enum UnrealGvasSchemaKind
{
    Generic,
    SackboyGeneral,
    SackboyImageSet,
    JediSettings,
    JediGameplay,
    HogwartsMetadata,
}

internal sealed record UnrealGvasSchema(
    UnrealGvasSchemaKind Kind,
    string Name,
    string Build,
    string Evidence,
    string Confidence)
{
    public bool IsTitleSchema => Kind != UnrealGvasSchemaKind.Generic;

    public static UnrealGvasSchema Detect(GvasFile file)
    {
        var names = GvasFile.Descendants(file.Properties).Select(node => node.Name).ToHashSet(StringComparer.Ordinal);
        var saveClass = file.Header.SaveClass;
        var sackboyVersion = file.Header is { EngineMajor: 4, EngineMinor: 25, EnginePatch: 1, PackageVersion: 518 };
        var jediVersion = file.Header is { EngineMajor: 4, EngineMinor: 25, EnginePatch: 0, PackageVersion: 518, CustomVersionCount: 56 };
        if (sackboyVersion && saveClass == "/Script/GingerBread.GingerbreadSaveUser" &&
            names.Contains("CollectabellsStored") && names.Contains("Checksum"))
            return new(UnrealGvasSchemaKind.SackboyGeneral, "Sackboy General", BuildText(file),
                "Save class, UE 4.25.1 header and verified currency/checksum property names match the supplied PS5 generations.", "Verified storage; game effects remain Unknown");
        if (sackboyVersion && saveClass == "/Script/GingerBread.GingerbreadSaveImageSet")
            return new(UnrealGvasSchemaKind.SackboyImageSet, "Sackboy image set", BuildText(file),
                "Save class and image metadata names match the supplied PS5 image companions.", "Verified storage; pixel layout remains Inferred");
        if (jediVersion && saveClass == "/Script/SwGame.SwGameUserSettingsSaveObject")
            return new(UnrealGvasSchemaKind.JediSettings, "Jedi user settings", BuildText(file),
                "Save class matches the supplied Fallen Order settings document.", "Verified storage");
        if (jediVersion && saveClass == "/Script/RsGameTechRT.RsSaveGameObject" &&
            names.Contains("CompressedActorRecords") && names.Contains("UncompressedActorRecordsSize") && names.Contains("Crc32"))
            return new(UnrealGvasSchemaKind.JediGameplay, "Jedi gameplay actor stream", BuildText(file),
                $"Save class {saveClass} includes the actor array, declared decoded size and explicit CRC field.", "Verified storage; actor semantics and checksum coverage remain Unknown");
        if (saveClass == HogwartsImage.SaveClass && names.Contains("RawDatabaseImage") &&
            file.Header is { EngineMajor: 4, EngineMinor: 27, EnginePatch: 2, PackageVersion: 524, CustomVersionCount: 72 })
            return new(UnrealGvasSchemaKind.HogwartsMetadata, "Hogwarts metadata envelope", BuildText(file),
                "PersistentData save class and raw database image names match the supplied PS5 generations.", "Verified storage; only allowlisted database cells are editable and game effects remain unverified");
        return new(UnrealGvasSchemaKind.Generic, "Generic GVAS", BuildText(file),
            "No complete title schema signature matched; the generic lossless property explorer is used.", "Unknown");
    }

    private static string BuildText(GvasFile file) =>
        $"UE {file.Header.EngineMajor}.{file.Header.EngineMinor}.{file.Header.EnginePatch}, package {file.Header.PackageVersion}, " +
        $"custom versions {file.Header.CustomVersionCount}";
}
