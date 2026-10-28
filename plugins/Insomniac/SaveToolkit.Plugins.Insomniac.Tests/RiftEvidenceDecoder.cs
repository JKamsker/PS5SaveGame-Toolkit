using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Insomniac.Tests;

/// <summary>Existing F1 research tests exercise the same SDK hook exposed to savediff.</summary>
internal sealed class RiftEvidenceDecoder : ISaveDecoder
{
    private readonly HookSaveDecoder _decoder = new(new Insomniac.RiftEvidenceDecoder());

    public string Id => _decoder.Id;
    public bool CanDecode(ISaveFile file) => _decoder.CanDecode(file);
    public SemanticTree Decode(ISaveFile file, DecodeLimits limits) => _decoder.Decode(file, limits);
}
