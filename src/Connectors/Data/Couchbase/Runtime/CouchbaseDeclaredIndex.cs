namespace Koan.Data.Connector.Couchbase.Runtime;

/// <summary>
/// One index the entity declared, resolved into the SQL++ terms this store will build it from.
///
/// <para>Resolved by the repository rather than the schema, because naming a value is entity knowledge — a
/// compiled mapping supplies the physical path, and an unmapped entity falls back to the same conventional
/// spelling its filters use. The schema takes the terms and speaks GSI.</para>
/// </summary>
internal sealed record CouchbaseDeclaredIndex(string Name, IReadOnlyList<string> Terms, bool Unique);
