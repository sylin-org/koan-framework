using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sora.Data.Abstractions.Naming;

namespace Sora.Data.SqlServer;

internal sealed class SqlServerNamingDefaultsProvider : INamingDefaultsProvider
{
    public string Provider => "sqlserver";
    public StorageNameResolver.Convention GetConvention(IServiceProvider services)
    {
        var opts = services.GetRequiredService<IOptions<SqlServerOptions>>().Value;
        return new StorageNameResolver.Convention(opts.NamingStyle, opts.Separator, NameCasing.AsIs);
    }
    public Func<Type, string?>? GetAdapterOverride(IServiceProvider services) => null;
}