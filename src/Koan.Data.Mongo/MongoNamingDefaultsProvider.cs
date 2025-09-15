using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions.Naming;

namespace Koan.Data.Mongo;

internal sealed class MongoNamingDefaultsProvider : INamingDefaultsProvider
{
    public string Provider => "mongo";
    public StorageNameResolver.Convention GetConvention(IServiceProvider services)
    {
        var opts = services.GetRequiredService<IOptions<MongoOptions>>().Value;
        return new StorageNameResolver.Convention(opts.NamingStyle, opts.Separator ?? ".", NameCasing.AsIs);
    }
    public Func<Type, string?>? GetAdapterOverride(IServiceProvider services)
    {
        var opts = services.GetRequiredService<IOptions<MongoOptions>>().Value;
        return opts.CollectionName;
    }
}