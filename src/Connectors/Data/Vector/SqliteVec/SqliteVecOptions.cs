namespace Koan.Data.Vector.Connector.SqliteVec;

/// <summary>
/// Options for the sqlite-vec durable in-process vector adapter. Automatic placement pairs with SQLite's
/// configured local store; set <see cref="ConnectionString"/> only when vectors belong elsewhere.
/// </summary>
public sealed class SqliteVecOptions
{
    public const string Section = Infrastructure.Constants.Configuration.Section;

    /// <summary>SQLite connection string. By default, pairs with SQLite's effective local store.</summary>
    public string ConnectionString { get; set; } = Infrastructure.Constants.Configuration.Automatic;

    /// <summary>Distance metric for the vec0 virtual table: <c>cosine</c> (default), <c>l2</c>, or <c>l1</c>.</summary>
    public string DistanceMetric { get; set; } = "cosine";
}
