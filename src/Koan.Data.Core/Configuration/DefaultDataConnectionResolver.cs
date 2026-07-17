using Koan.Data.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Koan.Data.Core.Configuration;

internal sealed class DefaultDataConnectionResolver(
    IConfiguration config,
    DataSourceRegistry sourceRegistry,
    Routing.DataProviderCatalog providers) : IDataConnectionResolver
{
    public string? Resolve(string providerId, string name)
    {
        var owner = providers.Find(providerId);
        try
        {
            return owner is null
                ? AdapterConnectionResolver.ResolveConnectionString(config, sourceRegistry, providerId, name)
                : AdapterConnectionResolver.ResolveConnectionString(
                    config,
                    sourceRegistry,
                    providerId,
                    name,
                    owner);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
