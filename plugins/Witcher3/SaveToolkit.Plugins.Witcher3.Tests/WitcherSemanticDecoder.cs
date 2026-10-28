using SaveToolkit.Sdk.Saves;
using SaveToolkit.Testing.Diff;

namespace SaveToolkit.Plugins.Witcher3.Tests;

/// <summary>Keep existing F1 research tests on the production F12 hook.</summary>
internal sealed class WitcherSemanticDecoder : ISaveDecoder
{
    private readonly HookSaveDecoder _decoder = new(new Witcher3.WitcherSemanticDecoder());
    public string Id => _decoder.Id;
    public bool CanDecode(ISaveFile file) => _decoder.CanDecode(file);
    public SemanticTree Decode(ISaveFile file, DecodeLimits limits) => _decoder.Decode(file, limits);
}
