using SaveToolkit.Codecs.Sqlite;
using SaveToolkit.Plugins.UnrealGvas.Hogwarts;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

internal static class HogwartsDiscoveryFixture
{
    public const string Prefix = "hogwarts/RawDatabaseImage/";

    public static ISaveFile Create()
    {
        using var database = SqliteImage.Create();
        database.Execute("""
            CREATE TABLE MiscDataDynamic(DataOwner TEXT, DataName TEXT, DataValue TEXT);
            INSERT INTO MiscDataDynamic VALUES('Player0','PerkPoints','4'),('OtherOwner','PerkPoints','99'),
                ('Player0','PageCollectedSynthetic','1'),('Player0','UnrelatedValue','7');
            CREATE TABLE PerkDynamic(PerkID TEXT, Level INTEGER);
            INSERT INTO PerkDynamic VALUES('SyntheticTalent',1),('UnmappedTalent',2);
            CREATE TABLE CollectionDynamic(CategoryID TEXT,SubcategoryID TEXT,ItemID TEXT,ItemState TEXT,UpdateTime INTEGER);
            INSERT INTO CollectionDynamic VALUES('Exploration','Hogwarts','LORE_Synthetic','Unknown',10),
                ('Exploration','Hogwarts','LORE_Synthetic','Obtained',20),
                ('UnmappedCategory','None','UnmappedItem','UnmappedState',30);
            CREATE TABLE MapLocationDataDynamic(MapLocationID TEXT,State INTEGER);
            INSERT INTO MapLocationDataDynamic VALUES('SyntheticPage',11);
            CREATE TABLE AchievementDynamic(AchievementID TEXT,Instances INTEGER,IsComplete INTEGER,OneOfEach TEXT,RewardsDelayed INTEGER);
            INSERT INTO AchievementDynamic VALUES('FGC_Synthetic',3,1,NULL,1);
            """);
        using var exclusive = SqliteImage.Create();
        exclusive.Execute("""
            CREATE TABLE MiscDataDynamic(DataOwner TEXT, DataName TEXT, DataValue TEXT);
            INSERT INTO MiscDataDynamic VALUES('Player0','PerkPoints','8');
            """);
        byte[] bytes = TitleFixture.Hogwarts(HogwartsImage.EncodeStored(database.Serialize()), HogwartsImage.EncodeStored(exclusive.Serialize()));
        return MemorySaveSet.SingleFile("save.sav", bytes, "PPSA01603", "HL0000");
    }
}
