using System.Data.Common;
using Koan.Data.Core.Configuration;
using FirebirdSql.Data.FirebirdClient;

namespace Koan.Data.Connector.Firebird;

internal sealed class FirebirdConnectionFactory : IDataProviderConnectionFactory
{
    public bool CanHandle(string provider) =>
        string.Equals(provider, Infrastructure.Constants.Provider, StringComparison.OrdinalIgnoreCase);

    public DbConnection Create(string connectionString) =>
        new FbConnection(FirebirdConnectionStrings.Normalize(connectionString).ConnectionString);
}
