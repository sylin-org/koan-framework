using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions.Naming;

namespace Koan.Data.Connector.ElasticSearch;

public sealed class ElasticSearchNamingDefaultsProvider : INamingDefaultsProvider
{
    public string Provider => "elasticsearch";

    public StorageNameResolver.Convention GetConvention(IServiceProvider services)
    {
        var opts = services.GetService<IOptions<ElasticSearchOptions>>()?.Value;
        var separator = opts?.IndexPrefix is not null ? "-" : "_";
        return new StorageNameResolver.Convention(StorageNamingStyle.EntityType, separator, NameCasing.Lower);
    }

    public Func<Type, string?>? GetAdapterOverride(IServiceProvider services) => null;
}

