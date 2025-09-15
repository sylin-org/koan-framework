using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions.Naming;

namespace Koan.Data.Postgres;

internal sealed class PostgresNamingDefaultsProvider : INamingDefaultsProvider
{
    public string Provider => "postgres";
    public StorageNameResolver.Convention GetConvention(IServiceProvider services)
    {
        var opts = services.GetRequiredService<IOptions<PostgresOptions>>().Value;
        return new StorageNameResolver.Convention(opts.NamingStyle, opts.Separator, NameCasing.AsIs);
    }
    public Func<Type, string?>? GetAdapterOverride(IServiceProvider services) => null;
}