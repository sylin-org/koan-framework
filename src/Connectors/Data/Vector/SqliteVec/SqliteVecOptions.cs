namespace Koan.Data.Vector.Connector.SqliteVec;

/// <summary>
/// Options for the sqlite-vec durable in-process vector adapter. Point <see cref="ConnectionString"/> at
/// the same SQLite file as the data adapter to get the "one .db holds rows and vectors" story.
/// </summary>
public sealed class SqliteVecOptions
{
    public const string Section = "Koan:Data:SqliteVec";

    /// <summary>SQLite connection string. Defaults to a local file beside the app.</summary>
    public string ConnectionString { get; set; } = "Data Source=koan-vectors.db";

    /// <summary>Distance metric for the vec0 virtual table: <c>cosine</c> (default), <c>l2</c>, or <c>l1</c>.</summary>
    public string DistanceMetric { get; set; } = "cosine";
}
