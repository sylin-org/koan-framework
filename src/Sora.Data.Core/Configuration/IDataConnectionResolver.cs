using Microsoft.Extensions.Configuration;

namespace Sora.Data.Core.Configuration;

public interface IDataConnectionResolver
{
    string? Resolve(string providerId, string name);
}

internal sealed class DefaultDataConnectionResolver(IConfiguration config) : IDataConnectionResolver
{
    public string? Resolve(string providerId, string name)
    {
    // Priority 1: Sora:Data:Sources:{name}:{providerId}:ConnectionString
    var cs = Sora.Core.Configuration.Read<string?>(config, $"Sora:Data:Sources:{name}:{providerId}:ConnectionString", null);
        if (!string.IsNullOrWhiteSpace(cs)) return cs;
        // Priority 2: ConnectionStrings:{name}
        cs = config.GetConnectionString(name);
        if (!string.IsNullOrWhiteSpace(cs)) return cs;
        return null;
    }
}
