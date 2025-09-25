using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Data.Abstractions.Naming;

namespace Koan.Data.OpenSearch;

public sealed class OpenSearchNamingDefaultsProvider : INamingDefaultsProvider
{
    public string Provider => "opensearch";

    public StorageNameResolver.Convention GetConvention(IServiceProvider services)
    {
        var opts = services.GetService<IOptions<OpenSearchOptions>>()?.Value;
        var separator = opts?.IndexPrefix is not null ? "-" : "_";
        return new StorageNameResolver.Convention(StorageNamingStyle.EntityType, separator, NameCasing.LowerCase);
    }

    public Func<Type, string?>? GetAdapterOverride(IServiceProvider services) => null;
}
