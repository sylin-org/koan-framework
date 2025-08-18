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
        var section = config.GetSection($"Sora:Data:Sources:{name}:{providerId}");
        var cs = section["ConnectionString"];
        if (!string.IsNullOrWhiteSpace(cs)) return cs;
        // Priority 2: ConnectionStrings:{name}
        cs = config.GetConnectionString(name);
        if (!string.IsNullOrWhiteSpace(cs)) return cs;
        return null;
    }
}
