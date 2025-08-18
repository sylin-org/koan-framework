using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Sora.Data.Cqrs;

/// <summary>
/// Shared helpers for Outbox adapters (config binding + connection string resolution).
/// </summary>
public static class OutboxConfig
{
    /// <summary>
    /// Bind adapter options from Sora:Cqrs:Outbox:{adapterName}.
    /// </summary>
    public static OptionsBuilder<TOptions> BindOutboxOptions<TOptions>(this IServiceCollection services, string adapterName)
        where TOptions : class
        => services.AddOptions<TOptions>().BindConfiguration($"Sora:Cqrs:Outbox:{adapterName}");

    /// <summary>
    /// Resolve a connection string using Sora conventions.
    /// Precedence: inline > Sora:Data:Sources:{name}:{provider}:ConnectionString > ConnectionStrings:{name}.
    /// </summary>
    public static string ResolveConnectionString(IConfiguration cfg, string provider, string? inline, string? name, string defaultName)
    {
        if (!string.IsNullOrWhiteSpace(inline)) return inline!;
        var n = string.IsNullOrWhiteSpace(name) ? defaultName : name!;
        var fromDataSources = cfg[$"Sora:Data:Sources:{n}:{provider}:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(fromDataSources)) return fromDataSources!;
        var fromConn = cfg.GetConnectionString(n);
        if (!string.IsNullOrWhiteSpace(fromConn)) return fromConn!;
        throw new InvalidOperationException($"No connection string resolved for provider '{provider}' and name '{n}'.");
    }
}
