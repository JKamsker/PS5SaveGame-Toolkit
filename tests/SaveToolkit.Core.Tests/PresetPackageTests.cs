using System.Text;
using System.Text.Json.Nodes;
using SaveToolkit.Core.Presets;

namespace SaveToolkit.Core.Tests;

public sealed class PresetPackageTests
{
    [Fact]
    public void TypedNumbersUnicodeAndSemanticIdentityRoundtripWithoutCoercion()
    {
        var preset = PresetTransferFixture.Preset();
        var bytes = PresetPackageCodec.Encode([preset]);
        var copy = PresetPackageCodec.Decode(bytes).Presets.Single();
        Assert.Equal(preset.Name, copy.Name);
        Assert.Equal(preset.Compatibility, copy.Compatibility with { TitleIds = preset.Compatibility.TitleIds });
        Assert.Equal(preset.CreatedAt, copy.CreatedAt);
        Assert.Equal(preset.UpdatedAt, copy.UpdatedAt);
        foreach (var pair in preset.Actions[0].Parameters)
            Assert.True(System.Text.Json.JsonElement.DeepEquals(pair.Value, copy.Actions[0].Parameters[pair.Key]));
        Assert.Equal("900719925474099312345678901", copy.Actions[0].Parameters["huge"].GetRawText());
        Assert.Equal("1.2300e+120", copy.Actions[0].Parameters["lexeme"].GetRawText());
        Assert.Equal(preset.Actions[0].Instances.Single(), copy.Actions[0].Instances.Single());
        Assert.Equal("PS5 Save Game Toolkit", PresetPackageCodec.Decode(bytes).Provenance.Producer);
    }

    [Theory]
    [InlineData("schemaVersion", "99", "unsupportedPresetPackage")]
    [InlineData("unknown", "save bytes", "invalidPresetPackage")]
    public void RejectsFutureAndForeignEnvelopes(string field, string value, string code)
    {
        var json = JsonNode.Parse(PresetPackageCodec.Encode([PresetTransferFixture.Preset()]))!;
        json[field] = value;
        Assert.Equal(code, Assert.Throws<PresetTransferException>(() => PresetPackageCodec.Decode(Encoding.UTF8.GetBytes(json.ToJsonString()))).Code);
    }

    [Theory]
    [InlineData("gameBuild")]
    [InlineData("capabilitySchemaVersion")]
    [InlineData("requiresExplicitInstanceResolution")]
    public void MissingCompatibilityFieldsCannotBecomeWildcards(string field)
    {
        var json = JsonNode.Parse(PresetPackageCodec.Encode([PresetTransferFixture.Preset()]))!;
        json["presets"]![0]!["compatibility"]!.AsObject().Remove(field);
        Assert.Equal("presetPackageMissingField", Assert.Throws<PresetTransferException>(() => PresetPackageCodec.Decode(Encoding.UTF8.GetBytes(json.ToJsonString()))).Code);
    }

    [Fact]
    public void RejectsMalformedDepthCountDuplicatesAndFieldDomains()
    {
        AssertInvalid("{");
        AssertInvalid("{\"schemaVersion\":\"1\",\"schemaVersion\":\"1\"}");
        AssertInvalid(new string('[', 17) + "0" + new string(']', 17));
        var json = JsonNode.Parse(PresetPackageCodec.Encode([PresetTransferFixture.Preset()]))!;
        var record = json["presets"]![0]!.DeepClone();
        json["presets"] = new JsonArray(Enumerable.Range(0, 257).Select(_ => record.DeepClone()).ToArray());
        Assert.Equal("presetPackageCount", Error(json).Code);
        json["presets"] = new JsonArray(record.DeepClone(), record.DeepClone());
        Assert.Equal("duplicatePackageId", Error(json).Code);
        json["presets"] = new JsonArray(record.DeepClone());
        json["presets"]![0]!["actions"]![0]!["parameters"] = new JsonObject(Enumerable.Range(0, 129)
            .Select(index => KeyValuePair.Create<string, JsonNode?>("p" + index, JsonValue.Create(1))));
        Assert.Equal("presetPackageFields", Error(json).Code);
        json["presets"]![0]!["actions"]![0]!["parameters"] = new JsonObject { ["arbitraryDocument"] = new JsonArray(1, 2, 3) };
        Assert.Equal("presetPackageParameter", Error(json).Code);
        json["presets"]![0]!["actions"]![0]!["parameters"] = new JsonObject();
        json["presets"]![0]!["actions"]![0]!["scopeKind"] = "physicalRow";
        Assert.Equal("presetPackageScope", Error(json).Code);
    }

    [Theory]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void AggregateTargetLimitIsCheckedBeforeTypedPackageMaterialization(int secondActionCount, bool valid)
    {
        var json = JsonNode.Parse(PresetPackageCodec.Encode([PresetTransferFixture.Preset()]))!;
        var actions = json["presets"]![0]!["actions"]!.AsArray();
        var second = actions[0]!.DeepClone();
        var reference = actions[0]!["instances"]![0]!.DeepClone();
        actions[0]!["instances"] = new JsonArray(Enumerable.Range(0, 128).Select(_ => reference.DeepClone()).ToArray());
        second["instances"] = new JsonArray(Enumerable.Range(0, secondActionCount).Select(_ => reference.DeepClone()).ToArray());
        actions.Add(second);
        if (valid) Assert.Equal(2, PresetPackageCodec.Decode(Encoding.UTF8.GetBytes(json.ToJsonString())).Presets.Single().Actions.Count);
        else Assert.Equal("presetPackageFields", Error(json).Code);
    }

    [Theory]
    [InlineData("stableId", 256)]
    [InlineData("kind", PresetContractValidation.MaxKeyLength)]
    [InlineData("label", PresetContractValidation.MaxMetadataLength)]
    [InlineData("role", PresetContractValidation.MaxMetadataLength)]
    [InlineData("path", PresetContractValidation.MaxMetadataLength)]
    [InlineData("build", PresetContractValidation.MaxMetadataLength)]
    [InlineData("provenance", PresetContractValidation.MaxMetadataLength)]
    public void SemanticProvenanceFieldsHaveExplicitDomains(string field, int limit)
    {
        var json = JsonNode.Parse(PresetPackageCodec.Encode([PresetTransferFixture.Preset()]))!;
        var reference = json["presets"]![0]!["actions"]![0]!["instances"]![0]!;
        reference[field] = new string('x', limit);
        Assert.NotNull(PresetPackageCodec.Decode(Encoding.UTF8.GetBytes(json.ToJsonString())));
        reference[field] = new string('x', limit + 1);
        Assert.Equal("presetPackageField", Error(json).Code);
    }

    [Fact]
    public void OpaqueResolutionCandidatesCannotEnterPortableReferences()
    {
        var json = JsonNode.Parse(PresetPackageCodec.Encode([PresetTransferFixture.Preset()]))!;
        json["presets"]![0]!["actions"]![0]!["instances"]![0]!["candidateStableId"] = "transient-choice";
        Assert.Equal("invalidPresetPackage", Error(json).Code);
    }

    [Fact]
    public async Task BoundedReaderRejectsDeclaredSizeBeforeReadingAndChunkedSizeAtLimit()
    {
        var ct = TestContext.Current.CancellationToken;
        using var untouched = new MemoryStream(new byte[1]);
        var declared = await Assert.ThrowsAsync<PresetTransferException>(() => PresetPackageCodec.ReadBoundedAsync(untouched, PresetPackageCodec.MaxBytes + 1, ct));
        Assert.Equal(413, declared.StatusCode);
        Assert.Equal(0, untouched.Position);
        using var large = new MemoryStream(new byte[PresetPackageCodec.MaxBytes * 2]);
        await Assert.ThrowsAsync<PresetTransferException>(() => PresetPackageCodec.ReadBoundedAsync(large, null, ct));
        Assert.Equal(PresetPackageCodec.MaxBytes + 1, large.Position);
    }

    private static void AssertInvalid(string json) => Assert.Equal("invalidPresetPackage",
        Assert.Throws<PresetTransferException>(() => PresetPackageCodec.Decode(Encoding.UTF8.GetBytes(json))).Code);
    private static PresetTransferException Error(JsonNode json) => Assert.Throws<PresetTransferException>(
        () => PresetPackageCodec.Decode(Encoding.UTF8.GetBytes(json.ToJsonString())));
}
