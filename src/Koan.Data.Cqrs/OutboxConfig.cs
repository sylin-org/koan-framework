using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Modules;

namespace Koan.Data.Cqrs;

/// <summary>
/// Shared helpers for Outbox adapters (config binding + connection string resolution).
/// </summary>
public static class OutboxConfig
{
    /// <summary>
    /// Bind adapter options from Koan:Cqrs:Outbox:{adapterName}.
    /// </summary>
    public static OptionsBuilder<TOptions> BindOutboxOptions<TOptions>(this IServiceCollection services, string adapterName)
        where TOptions : class
        => services.AddKoanOptions<TOptions>($"Koan:Cqrs:Outbox:{adapterName}");

    /// <summary>
    /// Resolve a connection string using Koan conventions.
    /// Precedence: inline > Koan:Data:Sources:{name}:{provider}:ConnectionString > ConnectionStrings:{name}.
    /// </summary>
    public static string ResolveConnectionString(IConfiguration cfg, string provider, string? inline, string? name, string defaultName)
    {
        if (!string.IsNullOrWhiteSpace(inline)) return inline!;
        var n = string.IsNullOrWhiteSpace(name) ? defaultName : name!;
        var fromDataSources = cfg[$"Koan:Data:Sources:{n}:{provider}:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(fromDataSources)) return fromDataSources!;
        var fromConn = cfg.GetConnectionString(n);
        if (!string.IsNullOrWhiteSpace(fromConn)) return fromConn!;
        throw new InvalidOperationException($"No connection string resolved for provider '{provider}' and name '{n}'.");
    }
}
