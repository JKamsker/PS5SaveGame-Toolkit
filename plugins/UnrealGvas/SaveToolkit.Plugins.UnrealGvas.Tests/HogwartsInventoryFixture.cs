using SaveToolkit.Codecs.Sqlite;
using SaveToolkit.Plugins.UnrealGvas.Hogwarts;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

internal static class HogwartsInventoryFixture
{
    public const string Collection = "hogwarts/RawDatabaseImage/InventoryDynamic";
    public const int TotalSlots = 410;
    public const int EmptySlots = 203;

    public static TestSaveImport CreateDisk() => new(Create().ReadAllBytes());

    public static ISaveFile Create()
    {
        using var database = SqliteImage.Create();
        database.Execute("""
            CREATE TABLE InventoryDynamic(CharacterID TEXT, HolderID TEXT, SlotNumber INTEGER, ItemID TEXT, Variation TEXT, Count INTEGER);
            WITH RECURSIVE slots(n) AS (VALUES(0) UNION ALL SELECT n+1 FROM slots WHERE n<404)
            INSERT INTO InventoryDynamic SELECT 'Player0','Bag',n,CASE WHEN n%2=0 THEN NULL ELSE 'UnknownItem' END,NULL,
                CASE WHEN n%2=0 THEN 0 ELSE n END FROM slots;
            INSERT INTO InventoryDynamic VALUES
                ('Player0','Bag',405,'UnmappedZero',NULL,0),
                ('Player0','Bag',406,'None',NULL,0),
                ('Player0','Bag',407,NULL,NULL,1),
                ('Player0','Bag',408,NULL,'unproven-variation',0),
                ('Player0','Bag',409,NULL,NULL,NULL);
            CREATE TABLE EquippedGearDynamic(ActorID TEXT, SlotID TEXT, GearID TEXT, GearVariation TEXT);
            INSERT INTO EquippedGearDynamic VALUES('Player0','Head','UnknownGear','None');
            """);
        byte[] frames = HogwartsImage.EncodeStored(database.Serialize());
        byte[] bytes = TitleFixture.Hogwarts(frames, frames);
        return MemorySaveSet.SingleFile("save.sav", bytes, "PPSA01603", "HL0000");
    }
}
