namespace Koan.Data.Relational.Npgsql;

/// <summary>Provider-native fallback order used when an Entity query supplies no explicit sort.</summary>
public enum NpgsqlStableOrder
{
    PostgreSqlPhysicalTuple,
    Identity
}
