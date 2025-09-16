using Microsoft.Extensions.Configuration;

namespace Koan.Data.Core.Configuration;

internal sealed class DefaultDataConnectionResolver(IConfiguration config) : IDataConnectionResolver
{
    public string? Resolve(string providerId, string name)
    {
        // Priority 1: Koan:Data:Sources:{name}:{providerId}:ConnectionString
        var cs = Koan.Core.Configuration.Read<string?>(config, $"Koan:Data:Sources:{name}:{providerId}:ConnectionString", null);
        if (!string.IsNullOrWhiteSpace(cs)) return cs;
        // Priority 2: ConnectionStrings:{name}
        cs = config.GetConnectionString(name);
        if (!string.IsNullOrWhiteSpace(cs)) return cs;
        return null;
    }
}