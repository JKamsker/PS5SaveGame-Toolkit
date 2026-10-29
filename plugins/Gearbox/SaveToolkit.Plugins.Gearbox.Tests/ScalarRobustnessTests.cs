using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Values;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class ScalarRobustnessTests
{
    [Theory]
    [InlineData(false, "experience_points", 2_147_483_648L)]
    [InlineData(true, "experience_points", 2_147_483_648L)]
    [InlineData(false, "experience_points", -1L)]
    [InlineData(true, "experience_points", -1L)]
    [InlineData(false, "time_played_seconds", long.MaxValue)]
    [InlineData(true, "time_played_seconds", long.MaxValue)]
    [InlineData(false, "time_played_seconds", 4_294_967_296L)]
    [InlineData(true, "time_played_seconds", -1L)]
    public void UnsupportedCharacterScalar_IsRejectedAtOpenWithFieldName(bool wonderlands, string name, long value)
    {
        var (container, root) = Create(wonderlands);
        root.SetInteger(name, value);
        var file = File(wonderlands, container.Encode(root));
        var error = Assert.Throws<SaveFormatException>(() => new GearboxPlugin().Open(file));
        Assert.Contains(name, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidScalarUpperBounds_OpenBuildAndRoundTrip(bool wonderlands)
    {
        var (container, root) = Create(wonderlands);
        root.SetInteger("experience_points", int.MaxValue);
        root.SetInteger("time_played_seconds", uint.MaxValue);
        var bytes = container.Encode(root);
        using var document = new GearboxPlugin().Open(File(wonderlands, bytes));
        _ = document.BuildView();
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
    }

    [Theory]
    [InlineData(false, 2_147_483_648L)]
    [InlineData(true, 2_147_483_648L)]
    [InlineData(false, -1L)]
    [InlineData(true, long.MaxValue)]
    public void UnsupportedSduLevel_IsReadOnlyAndLossless(bool wonderlands, long value)
    {
        var (container, root) = Create(wonderlands);
        var sdu = WriteAtomicityTests.AddMessage(root, "sdu_list");
        sdu.SetText("sdu_data_path", "/Game/Pickups/SDU/SDU_Backpack.SDU_Backpack");
        sdu.SetInteger("sdu_level", value);
        var bytes = container.Encode(root);
        using var document = new GearboxPlugin().Open(File(wonderlands, bytes));
        _ = document.BuildView();
        _ = document.ExpandNode("sdus");
        Assert.Equal(value, document.GetValue("sdu/0").AsInt64());
        Assert.False(document.ApplyEdits([new("sdu/0", FieldValue.Of(1L))]).Succeeded);
        Assert.Equal(bytes, document.Serialize().Single().Content.ToArray());
    }

    private static (GearboxContainer Container, ProtoMessage Root) Create(bool wonderlands)
    {
        var container = GearboxContainer.Parse(ContainerTests.Create(wonderlands, false), Title(wonderlands), false);
        return (container, container.Decode());
    }

    private static SaveToolkit.Sdk.Saves.ISaveFile File(bool wonderlands, byte[] bytes) =>
        new MemorySaveSet().With(Title(wonderlands), "TEST", new Dictionary<string, byte[]> { ["1"] = bytes }).Directories[0].Files[0];

    private static string Title(bool wonderlands) => wonderlands ? "PPSA01493" : "PPSA01463";
}
