using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions.Naming;

namespace Koan.Data.Connector.Couchbase;

internal sealed class CouchbaseNamingDefaultsProvider : INamingDefaultsProvider
{
    public string Provider => "couchbase";

    public StorageNameResolver.Convention GetConvention(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<CouchbaseOptions>>().Value;
        return new StorageNameResolver.Convention(options.NamingStyle, options.Separator ?? ".", NameCasing.AsIs);
    }

    public Func<Type, string?>? GetAdapterOverride(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<CouchbaseOptions>>().Value;
        if (!string.IsNullOrWhiteSpace(options.Collection))
        {
            return _ => options.Collection;
        }
        return options.CollectionName;
    }
}

