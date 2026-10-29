using SaveToolkit.Core.References;

namespace SaveToolkit.Plugins.Witcher3.Tests;

internal static class ReferencePackFixture
{
    public static ReferenceContext Pin(Witcher3Plugin? plugin = null, string titleId = "PPSA04018")
    {
        plugin ??= new Witcher3Plugin();
        var store = new ReferencePackStore([plugin]);
        // Includes every manifest hash and the framework's image-type validation.
        Assert.Empty(store.LoadErrors);
        return store.Pin(plugin, titleId);
    }
}
