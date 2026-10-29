using SaveToolkit.Sdk;
using SaveToolkit.Sdk.References;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.Gearbox.Tests;

public sealed class GroupOpenPerformanceTests
{
    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void OpenGroup_ReadsEachImmutableMemberOnceAndRetainsValidatedDocuments(string title)
    {
        var (character, profile) = Members(title);
        var input = new DocumentGroupInput([new("character", character), new("profile", profile)]);
        using var group = new GearboxPlugin().OpenGroup(input, IReferenceContext.Empty);
        Assert.Equal(1, character.FullReads);
        Assert.Equal(1, profile.FullReads);
        _ = group.BuildView();
        foreach (var output in group.Serialize())
        {
            var file = output.RelativePath == character.RelativePath ? character : profile;
            Assert.Equal(file.OriginalBytes(), output.Content.ToArray());
        }
        Assert.Equal(1, character.FullReads);
        Assert.Equal(1, profile.FullReads);
    }

    [Theory]
    [InlineData("PPSA01463", "character")]
    [InlineData("PPSA01463", "profile")]
    [InlineData("PPSA01493", "character")]
    [InlineData("PPSA01493", "profile")]
    public void OpenGroupAndValidation_StillRejectCorruptMembers(string title, string role)
    {
        var (character, profile) = Members(title);
        (role == "character" ? character : profile).CorruptPayload = true;
        var input = new DocumentGroupInput([new("character", character), new("profile", profile)]);
        var plugin = new GearboxPlugin();
        Assert.NotEmpty(plugin.ValidateGroup(input, IReferenceContext.Empty));
        Assert.Throws<SaveFormatException>(() => plugin.OpenGroup(input, IReferenceContext.Empty));
    }

    [Theory]
    [InlineData("PPSA01463")]
    [InlineData("PPSA01493")]
    public void InvalidRoles_AreRejectedBeforeAnyFullRead(string title)
    {
        var (character, profile) = Members(title);
        var input = new DocumentGroupInput([new("profile", character), new("character", profile)]);
        Assert.Throws<SaveFormatException>(() => new GearboxPlugin().OpenGroup(input, IReferenceContext.Empty));
        Assert.Equal(0, character.FullReads);
        Assert.Equal(0, profile.FullReads);
    }

    private static (CountingFile Character, CountingFile Profile) Members(string title)
    {
        var files = SampleSaves.Load(title).AllDiskFiles;
        var character = files.Where(f => f.FileName == "1").MaxBy(f => f.Length)!;
        var profile = files.Single(f => f.FileName == "profile" && f.Directory.Generation == character.Directory.Generation &&
            f.Directory.IsBackup == character.Directory.IsBackup);
        return (new(character), new(profile));
    }

    private sealed class CountingFile(ISaveFile inner) : ISaveFile
    {
        public int FullReads { get; private set; }
        public bool CorruptPayload { get; set; }
        public string Id => inner.Id;
        public ISaveDirectory Directory => inner.Directory;
        public string RelativePath => inner.RelativePath;
        public string PathInDirectory => inner.PathInDirectory;
        public string FileName => inner.FileName;
        public long Length => inner.Length;
        public Stream OpenRead() => new MemoryStream(ReadAllBytes(), writable: false);
        public byte[] ReadHeader(int count) => inner.ReadHeader(count);
        public byte[] OriginalBytes() => inner.ReadAllBytes();
        public byte[] ReadAllBytes()
        {
            FullReads++;
            var bytes = inner.ReadAllBytes();
            return CorruptPayload ? bytes[..^1] : bytes;
        }
    }
}
