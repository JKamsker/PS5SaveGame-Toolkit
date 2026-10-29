using System.Text.Json;
using SaveToolkit.Codecs.Sqlite;
using SaveToolkit.Plugins.UnrealGvas.Hogwarts;
using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.UnrealGvas.Tests;

public sealed class HogwartsTalentSummaryTests
{
    private const string PointSchema = "CREATE TABLE MiscDataDynamic(DataOwner TEXT COLLATE NOCASE, DataName TEXT COLLATE NOCASE, DataValue);";
    private const string PerkSchema = "CREATE TABLE PerkDynamic(PerkID TEXT, Level);";
    private const string PointsLabel = "Stored talent points";
    private const string RowsLabel = "Stored talent-record rows";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndependentAndMirroredImagesKeepTheirOwnValuesAndCounts(bool mirrored)
    {
        byte[] first = Image("004", 2);
        var sample = Save(first, mirrored ? first : Image("8", 3));
        using var document = new UnrealGvasPlugin().Open(sample);
        var sections = Summaries(document);
        Assert.Equal(2, sections.Length);
        Assert.StartsWith("RawDatabaseImage", sections[0].Title);
        Assert.StartsWith("RawExclusiveImage", sections[1].Title);
        Assert.Equal("SQLite text: \"004\"", Stat(sections[0], PointsLabel));
        Assert.Equal("2", Stat(sections[0], RowsLabel));
        Assert.Equal(mirrored ? "SQLite text: \"004\"" : "SQLite text: \"8\"", Stat(sections[1], PointsLabel));
        Assert.Equal(mirrored ? "2" : "3", Stat(sections[1], RowsLabel));
        Assert.Equal(!mirrored, Overview(document).OfType<TextNode>().Any(node => node.Text.StartsWith("Talent storage differs", StringComparison.Ordinal)));
        foreach (var section in sections)
        {
            Assert.DoesNotContain(section.Children, node => node is FieldNode or ActionNode);
            string note = Assert.Single(section.Children.OfType<TextNode>()).Text;
            Assert.Contains("Inferred", note);
            Assert.Contains("image role and player association are unverified", note);
            Assert.Contains("https://github.com/", note);
        }
        AssertNoOp(document, sample);
    }

    [Theory]
    [InlineData("4", 3)] // Same points, different record counts.
    [InlineData("5", 2)] // Same record counts, different points.
    [InlineData(4L, 2)] // Same spelling, different SQLite storage types.
    public void EitherComparableDifferenceProducesAnInformationalNote(object points, int rows)
    {
        using var document = new UnrealGvasPlugin().Open(Save(Image("4", 2), Image(points, rows)));
        var note = Assert.Single(Overview(document).OfType<TextNode>(), node =>
            node.Text.StartsWith("Talent storage differs", StringComparison.Ordinal));
        Assert.Equal(Tone.Info, note.Tone);
    }

    [Theory]
    [InlineData("DataOwner")]
    [InlineData("DataName")]
    [InlineData("DataValue")]
    public void MissingPointColumnsAreUnavailableAndDoNotHideValidTalentCounts(string column)
    {
        byte[] image = SqlImage(PointSchema.Replace(column, "UnknownColumn", StringComparison.Ordinal) + PerkSchema);
        using var document = new UnrealGvasPlugin().Open(Save(image));
        var section = Summaries(document)[0];
        Assert.Equal("Unavailable (unknown MiscDataDynamic columns)", Stat(section, PointsLabel));
        Assert.Equal("0", Stat(section, RowsLabel));
    }

    [Theory]
    [InlineData("PerkID")]
    [InlineData("Level")]
    public void UnknownTalentColumnsAreUnavailableInsteadOfZero(string column)
    {
        byte[] image = SqlImage(PerkSchema.Replace(column, "UnknownColumn", StringComparison.Ordinal));
        using var document = new UnrealGvasPlugin().Open(Save(image));
        Assert.Equal("Unavailable (unknown PerkDynamic columns)", Stat(Summaries(document)[0], RowsLabel));
    }

    [Fact]
    public void MissingTablesAndMissingTupleAreDistinctFromAnEmptyTalentTable()
    {
        byte[] absent = SqlImage("CREATE TABLE Unrelated(Value INTEGER);");
        byte[] empty = SqlImage(PointSchema + PerkSchema);
        using var document = new UnrealGvasPlugin().Open(Save(absent, empty));
        var sections = Summaries(document);
        Assert.Equal("Unavailable (MiscDataDynamic missing)", Stat(sections[0], PointsLabel));
        Assert.Equal("Unavailable (PerkDynamic missing)", Stat(sections[0], RowsLabel));
        Assert.Equal("Unavailable (Player0/PerkPoints missing)", Stat(sections[1], PointsLabel));
        Assert.Equal("0", Stat(sections[1], RowsLabel));
    }

    [Theory]
    [InlineData("OtherOwner", "PerkPoints")]
    [InlineData("player0", "PerkPoints")]
    [InlineData("Player0", "perkpoints")]
    public void TupleLookupIsExactEvenWhenSchemaDeclaresNoCase(string owner, string name)
    {
        byte[] image = SqlImage(PointSchema + PerkSchema, database => database.Execute(
            "INSERT INTO MiscDataDynamic VALUES($owner,$name,'99');",
            new Dictionary<string, object?> { ["$owner"] = owner, ["$name"] = name }));
        using var document = new UnrealGvasPlugin().Open(Save(image));
        Assert.Equal("Unavailable (Player0/PerkPoints missing)", Stat(Summaries(document)[0], PointsLabel));
    }

    [Fact]
    public void CaseVariantsDoNotMakeTheExactTupleAmbiguous()
    {
        byte[] image = Image("4", extraSql: "INSERT INTO MiscDataDynamic VALUES('player0','PerkPoints','99'),('Player0','perkpoints','98');");
        using var document = new UnrealGvasPlugin().Open(Save(image));
        Assert.Equal("SQLite text: \"4\"", Stat(Summaries(document)[0], PointsLabel));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1002)]
    public void DuplicateExactTuplesAreAmbiguousWithoutMaterializingEveryMatch(int count)
    {
        byte[] image = SqlImage(PointSchema + PerkSchema, database => database.Execute("""
            WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<$count)
            INSERT INTO MiscDataDynamic SELECT 'Player0','PerkPoints','4' FROM n;
            """.ReplaceLineEndings("\n"), new Dictionary<string, object?> { ["$count"] = count }));
        var sample = Save(image);
        using var document = new UnrealGvasPlugin().Open(sample);
        Assert.Equal("Ambiguous (multiple Player0/PerkPoints rows)", Stat(Summaries(document)[0], PointsLabel));
        AssertNoOp(document, sample);
    }

    [Theory]
    [InlineData(null, "Unavailable (SQL NULL)")]
    [InlineData(4L, "SQLite integer: 4")]
    [InlineData(1.5, "Unavailable (unexpected SQLite real)")]
    [InlineData("", "SQLite text: \"\"")]
    [InlineData(" 004 ", "SQLite text: \" 004 \"")]
    [InlineData("unknown", "SQLite text: \"unknown\"")]
    [InlineData(-1L, "SQLite integer: -1")]
    [InlineData("4\0extra", "Unavailable (SQLite text contains NUL)")]
    public void PointValuesRetainTheirStorageTypeAndExactText(object? value, string expected)
    {
        using var document = new UnrealGvasPlugin().Open(Save(Image(value)));
        Assert.Equal(expected, Stat(Summaries(document)[0], PointsLabel));
    }

    [Fact]
    public void BlobPointsAreUnavailableAndLongTextIsExplicitlyClipped()
    {
        var sample = Save(Image(new byte[] { 1, 2, 3 }), Image(new string('x', 100_000)));
        using var document = new UnrealGvasPlugin().Open(sample);
        var sections = Summaries(document);
        Assert.Equal("Unavailable (unexpected SQLite blob)", Stat(sections[0], PointsLabel));
        string value = Stat(sections[1], PointsLabel);
        Assert.Contains("[clipped to 128 characters]", value);
        Assert.True(value.Length < 256);
        AssertNoOp(document, sample);
    }

    [Fact]
    public void ClippedUnicodePointsStayBoundedWithoutClaimingAFullValueComparison()
    {
        string prefix = string.Concat(Enumerable.Repeat("\U0001F9D9", 128));
        var sample = Save(Image(prefix + "a"), Image(prefix + "b"));
        using var document = new UnrealGvasPlugin().Open(sample);
        foreach (var section in Summaries(document))
        {
            string value = Stat(section, PointsLabel);
            Assert.Contains("[clipped to 128 characters]", value);
            Assert.True(value.Length < 2048);
        }
        Assert.DoesNotContain(Overview(document).OfType<TextNode>(), node =>
            node.Text.StartsWith("Talent storage differs", StringComparison.Ordinal));
        AssertNoOp(document, sample);
    }

    [Fact]
    public void CountsAreRowsRegardlessOfDuplicateTalentIdsAndUnknownLevels()
    {
        byte[] image = Image("4", 0, "INSERT INTO PerkDynamic VALUES('Duplicate',1),('Duplicate',99),(NULL,NULL);");
        using var document = new UnrealGvasPlugin().Open(Save(image));
        Assert.Equal("3", Stat(Summaries(document)[0], RowsLabel));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidCompanionImageDoesNotHideTheSuccessfulSummary(bool badExclusive)
    {
        byte[] good = Image("4");
        byte[] bad = [0, 1, 2, 3];
        var sample = badExclusive ? Save(good, bad) : Save(bad, good);
        using var document = new UnrealGvasPlugin().Open(sample);
        Assert.False(document.CanEdit);
        var section = Assert.Single(Summaries(document));
        Assert.StartsWith(badExclusive ? "RawDatabaseImage" : "RawExclusiveImage", section.Title);
        Assert.Equal("SQLite text: \"4\"", Stat(section, PointsLabel));
        Assert.Contains(Overview(document).OfType<TextNode>(), text => text.Text.Contains("overlay unavailable", StringComparison.Ordinal));
        AssertNoOp(document, sample);
    }

    [Fact]
    public void RepeatedSummariesKeepExistingBindingsReadOnlyAndBytesIdentical()
    {
        var sample = HogwartsDiscoveryFixture.Create();
        using var document = new UnrealGvasPlugin().Open(sample);
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        var before = queryable.Query(HogwartsDiscoveryFixture.Prefix + "MiscDataDynamic", new());
        var fields = before.Rows.SelectMany(row => row.Cells.Values).OfType<FieldNode>().ToArray();
        string view = JsonSerializer.Serialize(document.BuildView());
        Assert.Equal(view, JsonSerializer.Serialize(document.BuildView()));
        var after = queryable.Query(before.CollectionId, new());
        Assert.Equal(fields.Select(field => field.ValueId), after.Rows.SelectMany(row => row.Cells.Values).OfType<FieldNode>().Select(field => field.ValueId));
        foreach (var field in fields) Assert.Equal(field.Value, document.GetValue(field.ValueId));
        var points = fields[0];
        Assert.True(points.Spec.ReadOnly);
        Assert.False(document.ApplyEdits([new(points.ValueId, points.Value)]).Succeeded);
        AssertNoOp(document, sample);
    }

    private static byte[] Image(object? value, int talentRows = 2, string extraSql = "") =>
        SqlImage(PointSchema + PerkSchema, database =>
        {
            database.Execute("INSERT INTO MiscDataDynamic VALUES('Player0','PerkPoints',$value);",
                new Dictionary<string, object?> { ["$value"] = value });
            for (int index = 0; index < talentRows; index++)
                database.Execute("INSERT INTO PerkDynamic VALUES($id,1);", new Dictionary<string, object?> { ["$id"] = "Synthetic" + index });
            if (extraSql.Length > 0) database.Execute(extraSql);
        });

    private static byte[] SqlImage(string schema, Action<SqliteImage>? seed = null)
    {
        using var database = SqliteImage.Create();
        database.Execute(schema);
        seed?.Invoke(database);
        return database.Serialize();
    }

    private static ISaveFile Save(byte[] database, byte[]? exclusive = null) => MemorySaveSet.SingleFile("save.sav",
        TitleFixture.Hogwarts(HogwartsImage.EncodeStored(database), HogwartsImage.EncodeStored(exclusive ?? database)), "PPSA01603");

    private static IReadOnlyList<ViewNode> Overview(ISaveDocument document) =>
        Assert.IsType<TabsNode>(document.BuildView()).Tabs[0].Children;

    private static SectionNode[] Summaries(ISaveDocument document) => Overview(document).OfType<SectionNode>().ToArray();

    private static string Stat(SectionNode section, string label) =>
        Assert.Single(Assert.Single(section.Children.OfType<StatsNode>()).Items, item => item.Label == label).Value;

    private static void AssertNoOp(ISaveDocument document, ISaveFile sample) =>
        Assert.Equal(sample.ReadAllBytes(), Assert.Single(document.Serialize()).Content.ToArray());
}
