namespace SaveToolkit.Plugins.FinalFantasy16;

// Describes signed storage only; it does not establish ownership, vacancy or gameplay validity.
internal static class FaithStoredStackState
{
    public const string ColumnKey = "stackState";
    public const string Positive = "Positive";
    public const string Zero = "Zero";
    public const string Negative = "Negative";

    public static string Label(int quantity) => quantity switch
    {
        > 0 => Positive,
        0 => Zero,
        _ => Negative,
    };
}
