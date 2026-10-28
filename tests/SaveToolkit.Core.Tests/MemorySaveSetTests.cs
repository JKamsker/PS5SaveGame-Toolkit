using SaveToolkit.Testing;

namespace SaveToolkit.Core.Tests;

public sealed class MemorySaveSetTests
{
    [Fact]
    public void FileContent_DoesNotChangeWhenCallerMutatesItsInput()
    {
        byte[] bytes = [1, 2, 3];
        var file = MemorySaveSet.SingleFile("save.dat", bytes);

        bytes[0] = 99;

        Assert.Equal([1, 2, 3], file.ReadAllBytes());
        Assert.Equal([1, 2], file.ReadHeader(2));
        using var stream = file.OpenRead();
        Assert.False(stream.CanWrite);
        Assert.Equal(1, stream.ReadByte());
    }

    [Fact]
    public void ReturnedCopies_CannotChangeStoredFileContent()
    {
        var file = MemorySaveSet.SingleFile("save.dat", [1, 2, 3]);
        file.ReadAllBytes()[0] = 99;
        file.ReadHeader(2)[0] = 99;

        Assert.Equal([1, 2, 3], file.ReadAllBytes());
    }
}
