using System.Xml.Linq;
using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests;

public sealed class SdkDocumentationTests
{
    [Fact]
    public void EveryPublicSdkType_HasAnXmlSummaryInTheShippedDocumentation()
    {
        var assembly = typeof(IGamePlugin).Assembly;
        var documentation = XDocument.Load(Path.ChangeExtension(assembly.Location, ".xml"));
        var documented = documentation.Descendants("member")
            .Where(member => !string.IsNullOrWhiteSpace(member.Element("summary")?.Value))
            .Select(member => (string)member.Attribute("name")!)
            .ToHashSet(StringComparer.Ordinal);
        var missing = assembly.GetExportedTypes()
            .Where(type => !documented.Contains("T:" + type.FullName!.Replace('+', '.')))
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(missing.Length == 0, "Missing SDK type summaries: " + string.Join(", ", missing));
    }
}
