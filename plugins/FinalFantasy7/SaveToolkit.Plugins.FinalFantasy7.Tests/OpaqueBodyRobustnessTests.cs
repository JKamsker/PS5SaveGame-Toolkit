using SaveToolkit.Sdk.Collections;
using SaveToolkit.Sdk.Views;
using SaveToolkit.Testing;

namespace SaveToolkit.Plugins.FinalFantasy7.Tests;

public sealed class OpaqueBodyRobustnessTests
{
    [Theory]
    [InlineData("PPSA01911", false)]
    [InlineData("PPSA08664", false)]
    [InlineData("PPSA18936", false)]
    [InlineData("PPSA01911", true)]
    [InlineData("PPSA08664", true)]
    [InlineData("PPSA18936", true)]
    public void OpaqueBodiesAndSlotBytesStayBoundedAndLossless(string title, bool randomBodies)
    {
        const int Seed = 0xFF7;
        const int MaximumPageSize = 200;
        var fixture = SyntheticGameplay.Create(title);
        var random = new Random(Seed);
        foreach (var record in fixture.Records.Where(record => record.Kind != 4))
        {
            var body = fixture.Bytes.AsSpan(record.Offset + SyntheticGameplay.BodyOffset,
                record.Length - SyntheticGameplay.BodyOffset);
            if (randomBodies) random.NextBytes(body);
            else body.Fill(byte.MaxValue); // Includes extreme signed/unsigned length-like words and NaNs.
            if (record.Offset != 0)
            {
                // Unknown slot/index bytes are metadata, never array indexes or allocation counts.
                fixture.Bytes[record.Offset + 1] = byte.MaxValue;
            }
        }

        var schema = Ff7Schema.ForTitle(title)!;
        var file = MemorySaveSet.SingleFile(schema.Prefix + "000", fixture.Bytes, title);
        using var document = new FinalFantasy7Plugin().Open(file);
        Assert.IsType<TabsNode>(document.BuildView());
        var queryable = Assert.IsAssignableFrom<IQueryableDocument>(document);
        foreach (var collection in queryable.Collections)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            for (var offset = 0; offset < collection.TotalCount; offset += MaximumPageSize)
            {
                var page = queryable.Query(collection.Id,
                    new() { Offset = offset, Limit = MaximumPageSize, IncludeEmpty = true });
                Assert.Equal(collection.TotalCount, page.TotalCount);
                Assert.Equal(collection.TotalCount, page.FilteredCount);
                Assert.InRange(page.Rows.Count, 1, MaximumPageSize);
                Assert.All(page.Rows, row => Assert.True(visited.Add(row.Key)));
            }
            Assert.Equal(collection.TotalCount, visited.Count);
        }

        if (!schema.IsRemake)
        {
            for (var character = 0; character < Ff7Schema.CharacterCount; character++)
            {
                Assert.NotEmpty(document.ExpandNode($"spatial/sockets/{character}"));
                Assert.NotEmpty(document.ExpandNode($"spatial/folio/{character}"));
            }
        }
        Assert.Equal(fixture.Bytes, Assert.Single(document.Serialize()).Content.ToArray());
        Assert.Equal(fixture.Bytes, file.ReadAllBytes());
    }
}
