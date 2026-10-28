namespace SaveToolkit.Plugins.AstroPlayroom;

internal static class AstroWriteWarnings
{
    public const string Experimental =
        "Experimental: not validated on console. Game limits and linked progress effects are unverified.";

    public const string Integrity =
        "Integrity is unverified: no game checksum/signature algorithm is known; the game may reject an edited save.";

    public const string Collection =
        "Inferred from existing IsGot boolean fields and mixed-event sample differences. Only IsGot is set to true; " +
        "cinematic, notification, Progress/World and UDS fields are preserved. Pickup dependencies and rewards are " +
        "unverified; no completion or trophy guarantee is made.";
}
