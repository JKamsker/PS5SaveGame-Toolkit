using SaveToolkit.Sdk;

namespace SaveToolkit.Core.Tests;

public sealed class PresetOptionalMetadataConsistencyTests
{
    private static readonly string[] Fields = ["gameBuild", "formatVariant", "referencePackIdentity", "schemaFingerprint", "fileRole",
        "capabilitySchemaVersion", "label", "role", "path", "build", "provenance"];
    private static readonly string[] Values = ["", " ", "line one\nline two"];
    public static TheoryData<string, string> AcceptedMetadata => new(
        from name in Fields from value in Values select (name, value));

    [Theory]
    [MemberData(nameof(AcceptedMetadata))]
    public void StoredOptionalMetadataSurvivesTransferWithoutNormalization(string field, string value)
    {
        var template = PresetTransferFixture.Preset();
        var compatibility = field switch
        {
            "gameBuild" => template.Compatibility with { GameBuild = value },
            "formatVariant" => template.Compatibility with { FormatVariant = value },
            "referencePackIdentity" => template.Compatibility with { ReferencePackIdentity = value },
            "schemaFingerprint" => template.Compatibility with { SchemaFingerprint = value },
            "fileRole" => template.Compatibility with { FileRole = value },
            "capabilitySchemaVersion" => template.Compatibility with { CapabilitySchemaVersion = value },
            _ => template.Compatibility,
        };
        var instance = template.Actions[0].Instances[0];
        instance = field switch
        {
            "label" => instance with { Label = value },
            "role" => instance with { Role = value },
            "path" => instance with { Path = value },
            "build" => instance with { Build = value },
            "provenance" => instance with { Provenance = value },
            _ => instance,
        };
        var store = PresetTransferFixture.Store();
        ToolkitPreset saved = store.Create(template.Name, compatibility, [template.Actions[0] with { Instances = [instance] }]);
        PresetMetadataLimitConsistencyTests.AssertTransferAndReopen(store, saved);
    }
}
