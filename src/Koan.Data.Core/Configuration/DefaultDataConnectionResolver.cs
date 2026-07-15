using Koan.Data.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Koan.Data.Core.Configuration;

internal sealed class DefaultDataConnectionResolver(
    IConfiguration config,
    DataSourceRegistry sourceRegistry,
    IEnumerable<IDataAdapterFactory> factories) : IDataConnectionResolver
{
    public string? Resolve(string providerId, string name)
    {
        var owner = factories.FirstOrDefault(factory => factory.CanHandle(providerId));
        try
        {
            return owner is null
                ? AdapterConnectionResolver.ResolveConnectionString(config, sourceRegistry, providerId, name)
                : AdapterConnectionResolver.ResolveConnectionString(config, sourceRegistry, providerId, name, owner.CanHandle);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
