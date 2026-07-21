using System.Data.Common;

namespace Koan.Data.Core.Configuration;

/// <summary>
/// Creates provider-specific <see cref="DbConnection"/> instances.
/// Implementations live in data adapter packages (e.g., SqlServer, Postgres, Sqlite).
/// </summary>
public interface IDataProviderConnectionFactory
{
    /// <summary>Whether this factory can handle the given provider identifier (e.g., "sqlserver", "postgres", "sqlite").</summary>
    bool CanHandle(string provider);

    /// <summary>Create a closed DbConnection for the given connection string.</summary>
    DbConnection Create(string connectionString);

    /// <summary>
    /// Resolve one logical source through the provider's normal configuration/discovery path. Providers can opt in
    /// so Direct operations share the same physical route as repositories and readiness; null preserves the legacy
    /// concrete-connection fallback for existing implementations.
    /// </summary>
    string? ResolveConnectionString(string source) => null;

    /// <summary>
    /// Create a closed connection for a logical source. Providers whose physical connection lifetime depends on
    /// source identity can override this overload; existing providers retain connection-string-only behavior.
    /// </summary>
    DbConnection Create(string connectionString, string source)
        => Create(connectionString);
}
