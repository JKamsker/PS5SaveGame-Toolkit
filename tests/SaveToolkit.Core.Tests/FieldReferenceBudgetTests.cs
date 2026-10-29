using System.Net;
using System.Text.Json;
using SaveToolkit.Core.Editing;
using SaveToolkit.Sdk.Views;

namespace SaveToolkit.Core.Tests;

public sealed class FieldReferenceBudgetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferenceBearingOutputStillEnforcesTraversalBudgetRegardlessOfReferencePosition(bool referenceFirst)
    {
        using var document = new FieldReferenceDocument();
        var children = Enumerable.Repeat<ViewNode>(new TextNode("ordinary"), 100_001).ToList();
        children.Insert(referenceFirst ? 0 : children.Count, new FieldReferenceNode("quantity"));
        var root = new SectionNode("Bounded references", children);
        var index = new ViewIndex(document);
        index.Add(root);
        Assert.Contains("traversal", Assert.Throws<InvalidOperationException>(() => index.ResolveFieldReferences(document, root)).Message, StringComparison.Ordinal);
        Assert.Empty(document.MetadataRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalizedFullValuesAndCanonicalAnnotationsAreBudgetedInsteadOfTinyProviderReferences(bool oversizedWarning)
    {
        using var document = new FieldReferenceBudgetDocument(oversizedWarning ? "small value" : new string('"', 4000),
            oversizedWarning ? new string('"', 4000) : null);
        var root = new SectionNode("References", FieldReferenceBudgetDocument.References(200));
        Assert.True(JsonSerializer.SerializeToUtf8Bytes<ViewNode>(root, ToolkitJson.Options).Length < 20_000);
        var index = new ViewIndex(document);
        index.Add(root);
        Assert.Throws<FieldReferenceResponseLimitException>(() => index.ResolveFieldReferences(document, root));
        Assert.Equal(new byte[] { 1 }, document.Serialize().Single().Content.ToArray());
    }

    [Fact]
    public void UnderBudgetValuesStayExactRatherThanBeingClipped()
    {
        var text = new string('"', 4000);
        using var document = new FieldReferenceBudgetDocument(text);
        var root = new SectionNode("References", FieldReferenceBudgetDocument.References(2));
        var index = new ViewIndex(document);
        index.Add(root);
        var resolved = Assert.IsType<SectionNode>(index.ResolveFieldReferences(document, root));
        Assert.All(resolved.Children, node => Assert.Equal(text, Assert.IsType<FieldNode>(node).Value.AsString()));
    }

    [Theory]
    [InlineData("/collections?collectionId=items&limit=200")]
    [InlineData("/nodes?id=more")]
    public async Task HostRejectsOversizedNormalizedQueryAndExpansionWith413AndNoPartialBody(string suffix)
    {
        using var fixture = new ApiContractFixture(new FieldReferenceBudgetPlugin());
        var (_, document) = await fixture.OpenAsync();
        using var response = await fixture.Client.GetAsync(document + suffix, ApiContractFixture.Ct);
        var body = await ApiProblemContractTests.AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge);
        Assert.Contains("resolved field-reference view", body.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.False(body.TryGetProperty("rows", out _));
        Assert.False(body.TryGetProperty("view", out _));
        using var current = await fixture.Client.GetAsync(document, ApiContractFixture.Ct);
        current.EnsureSuccessStatusCode();
        using var currentBody = JsonDocument.Parse(await current.Content.ReadAsStringAsync(ApiContractFixture.Ct));
        Assert.False(currentBody.RootElement.GetProperty("state").GetProperty("isDirty").GetBoolean());
    }
}
