using SaveToolkit.Core.Presets;

namespace SaveToolkit.LocalApi.Endpoints;

/// <summary>
/// The preset store and its transfer service. The host loads them at startup, but a library this version
/// cannot read (malformed, or saved by a newer version) must not stop the host: like the ASP.NET host's
/// DI singletons, which are created on first use, it then fails only the requests that need presets, with
/// the same problem response as the Api, and leaves the file untouched. Every later use tries again, so a
/// repaired (or removed) file is picked up without a restart.
/// </summary>
internal sealed class PresetLibrary(Func<PresetStore> create)
{
    private readonly object _gate = new();
    private PresetStore? _store;
    private PresetTransferService? _transfers;

    public PresetStore Store => Load().Store;

    public PresetTransferService Transfers => Load().Transfers;

    /// <summary>Loads the library now when it is readable; otherwise leaves the failure to the requests that need it.</summary>
    public void TryLoad()
    {
        try { _ = Load(); }
        catch (Exception ex) when (ex is InvalidDataException or PresetStorageException or IOException or UnauthorizedAccessException)
        {
            // Reported by every preset request until the library can be read.
        }
    }

    /// <summary>
    /// After another writer may have replaced the files: a loaded store re-reads them (throwing, with its
    /// last good view kept, when they are unreadable). One never loaded has nothing cached to refresh.
    /// </summary>
    public void Refresh()
    {
        PresetStore? store;
        lock (_gate) store = _store;
        store?.Refresh();
    }

    private (PresetStore Store, PresetTransferService Transfers) Load()
    {
        lock (_gate)
        {
            if (_store is null)
            {
                var store = create();
                _transfers = new PresetTransferService(store);
                _store = store;
            }

            return (_store, _transfers!);
        }
    }
}
