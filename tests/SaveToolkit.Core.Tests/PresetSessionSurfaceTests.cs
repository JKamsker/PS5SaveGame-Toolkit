using SaveToolkit.Core.Sessions;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests;

public sealed class PresetSessionSurfaceTests
{
    [Fact]
    public void SessionDoesNotPublishMutablePresetAdapter()
    {
        Assert.DoesNotContain(typeof(DocumentSession).GetProperties(),
            property => typeof(IPresetDocument).IsAssignableFrom(property.PropertyType));
    }
}
