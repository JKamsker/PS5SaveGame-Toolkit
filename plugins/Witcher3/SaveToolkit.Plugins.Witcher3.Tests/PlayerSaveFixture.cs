namespace SaveToolkit.Plugins.Witcher3.Tests;

internal static class PlayerSaveFixture
{
    public static (WitcherContainer Container, Sav3Directory Directory, Sav3Tree Tree, PlayerData Player) Read(byte[] bytes) =>
        Read(WitcherContainer.Parse(bytes));

    public static (WitcherContainer Container, Sav3Directory Directory, Sav3Tree Tree, PlayerData Player) Read(WitcherContainer container)
    {
        var directory = Sav3Directory.Parse(container);
        var tree = Sav3Tree.Parse(container.Payload, directory);
        var player = Assert.IsType<PlayerData>(PlayerData.Read(container, directory, tree));
        return (container, directory, tree, player);
    }
}
