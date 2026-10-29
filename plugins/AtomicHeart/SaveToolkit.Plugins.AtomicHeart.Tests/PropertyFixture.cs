namespace SaveToolkit.Plugins.AtomicHeart.Tests;

/// <summary>Test-only inspection of authored property headers; independent of patch assembly.</summary>
internal static class PropertyFixture
{
    internal static int SizeOffset(byte[] plain, int tagStart)
    {
        var cursor = SaveExtensionBinary.Cursor(plain, tagStart);
        _ = cursor.String();
        _ = cursor.String();
        return cursor.Position;
    }
}
