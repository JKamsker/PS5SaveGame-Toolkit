namespace SaveToolkit.Plugins.FinalFantasy7;

internal static class WriteWarnings
{
    public const string Experimental = "Not validated on console. Internal integrity is unconfirmed; the game may reject the edited save. Keep a backup. ";
    public const string Progress = Experimental + "Only this candidate bit changes. Rewards, actors, counters, prerequisites and chapter-reset dependencies are not repaired; inconsistent story state or softlocks are possible. ";
    public const string Progression = Experimental + "Based on pinned MIT Game Tools Collection curves and PS5 sample offsets. Cached stats, abilities and progression effects may be recalculated by the game. " + EmbeddedCatalogs.Attribution;
}
