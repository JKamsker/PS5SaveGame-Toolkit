using SaveToolkit.Sdk;
using SaveToolkit.Sdk.Saves;
using SaveToolkit.Sdk.References;

namespace SaveToolkit.Plugins.AtomicHeart;

public sealed class AtomicHeartPlugin : IGamePlugin
{
    private const int ProbeHeaderBytes = 4096;
    public PluginInfo Info { get; } = new("atomic-heart", "Atomic Heart", "0.3.0")
    {
        Description = "Experimental existing inventory edits with stored Kraken output, SHA-1 repair and explicit console-compatibility warnings.",
        Games = [new GameTitle("PPSA10695", "Atomic Heart")],
        Maturity = PluginMaturity.Experimental,
        References = [
            "https://github.com/PipeRift/SaveExtension/blob/c40f37a078ce8b9b2b3a811e47729e6ca2d11d48/Source/SaveExtension/Private/FileAdapter.cpp",
            "https://github.com/PipeRift/SaveExtension/blob/c40f37a078ce8b9b2b3a811e47729e6ca2d11d48/Source/SaveExtension/Private/Serialization/Records.cpp",
            "https://github.com/PipeRift/SaveExtension/blob/c40f37a078ce8b9b2b3a811e47729e6ca2d11d48/Source/SaveExtension/Private/SlotData.cpp",
            "https://github.com/PipeRift/SaveExtension/blob/c40f37a078ce8b9b2b3a811e47729e6ca2d11d48/Source/SaveExtension/Private/Serialization/LevelRecords.cpp",
            "https://github.com/Skorchekd/AtomicHeartMenu/blob/fe36f468baacc996c252a1d3231d0d20009127bd/src/features/features.cpp",
            "https://github.com/Skorchekd/AtomicHeartMenu/blob/fe36f468baacc996c252a1d3231d0d20009127bd/NOTICE",
            "https://github.com/AaryanKh/TekkenMusicModManager/blob/8737709e840c74efc935e23ac0d33cdb6aaef8f5/src/Tmm.Core/Pak/Kraken.cs",
            "https://github.com/powzix/ooz/blob/05038060aa68f9187ae9923b2388ca8db40e58d1/kraken.cpp",
            "https://github.com/producedbytmsh/hl-savetools/blob/e568dedcda2611f54cc793e6d0237c749ae25094/hlsave.py",
            "https://www.rfc-editor.org/rfc/rfc3174",
        ],
    };

    public ProbeResult Probe(ISaveFile file)
    {
        if (file.Length is < 64 or > SaveExtensionFile.MaxBytes) return ProbeResult.None;
        try
        {
            var header = file.ReadHeader(ProbeHeaderBytes).AsSpan();
            return file.Directory.TitleId == "PPSA10695" &&
                header.StartsWith(new byte[] { 1, 0, 0, 0, 2, 0, 0, 0 }) &&
                header.IndexOf("/Script/SaveExtension.SlotInfo\0"u8) >= 0
                ? ProbeResult.Match(ProbeScore.Title, "Atomic Heart slot (SaveExtension)") : ProbeResult.None;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException) { return ProbeResult.None; }
    }

    public ISaveDocument Open(ISaveFile file) => Open(file, IReferenceContext.Empty);

    public ISaveDocument Open(ISaveFile file, IReferenceContext references)
    {
        if (file.Length > SaveExtensionFile.MaxBytes) throw new SaveFormatException("SaveExtension exceeds the 128 MiB limit.");
        var bytes = file.ReadAllBytes();
        var parsed = SaveExtensionFile.Parse(bytes);
        var index = SlotDataDecoder.Decode(bytes, parsed);
        return new AtomicHeartDocument(file.RelativePath, bytes, parsed, index, references);
    }
}
