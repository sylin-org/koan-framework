using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions.Naming;

namespace Koan.Data.Sqlite;

internal sealed class SqliteNamingDefaultsProvider : INamingDefaultsProvider
{
    public string Provider => "sqlite";
    public StorageNameResolver.Convention GetConvention(IServiceProvider services)
    {
        var opts = services.GetRequiredService<IOptions<SqliteOptions>>().Value;
        return new StorageNameResolver.Convention(opts.NamingStyle, opts.Separator, NameCasing.AsIs);
    }
    public Func<Type, string?>? GetAdapterOverride(IServiceProvider services)
        => null; // no override: honor [StorageName] and resolver conventions
}