using System.Security.Cryptography;
using SaveToolkit.Core.Sessions;
using SaveToolkit.Core.Sessions.Groups;
using SaveToolkit.Sdk.Saves;

namespace SaveToolkit.Core.Tests;

public sealed class SystemOutputR5Tests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("unauthorized")]
    public void GroupFreezeReadAfterFingerprint_MapsInterveningSourceFailure(string failureKind)
    {
        const string path = "PPSA99990/sdimg_SAVE/profile.dat";
        var bytes = new byte[] { 1, 2, 3 };
        var source = new SequencedReadFile(path, bytes,
            failureKind == "missing"
                ? new FileNotFoundException("removed")
                : new UnauthorizedAccessException("denied"));

        _ = OwnedSourceReader.Hash(source, path, CancellationToken.None);

        var member = new GroupMemberInfo("profile", source.Id, path,
            Convert.ToHexStringLower(SHA256.HashData(bytes)), false);
        var error = Assert.Throws<InvalidOperationException>(() =>
            DocumentGroupSession.FreezeMembers([member], _ => source, CancellationToken.None));

        Assert.Contains(path, error.Message, StringComparison.Ordinal);
        Assert.IsType(failureKind == "missing"
            ? typeof(FileNotFoundException)
            : typeof(UnauthorizedAccessException), error.InnerException);
    }

    private sealed class SequencedReadFile(string path, byte[] bytes, Exception secondReadFailure) : ISaveFile
    {
        private int _opens;
        public string Id => path;
        public ISaveDirectory Directory => null!;
        public string RelativePath => path;
        public string PathInDirectory => path[(path.LastIndexOf('/') + 1)..];
        public string FileName => PathInDirectory;
        public long Length => bytes.Length;
        public Stream OpenRead() => Interlocked.Increment(ref _opens) == 1
            ? new MemoryStream(bytes, writable: false)
            : throw secondReadFailure;
        public byte[] ReadAllBytes() => bytes.ToArray();
        public byte[] ReadHeader(int count) => bytes.AsSpan(0, Math.Min(count, bytes.Length)).ToArray();
    }
}
