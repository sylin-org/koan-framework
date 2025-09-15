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
}
