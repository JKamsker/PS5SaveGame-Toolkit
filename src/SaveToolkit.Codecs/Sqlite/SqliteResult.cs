namespace SaveToolkit.Codecs.Sqlite;

/// <summary>Bounded materialized values: null, Int64, Double, String, or a copied byte array.</summary>
public sealed record SqliteResult(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows);
