using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Koan.Data.Connector.Json.Infrastructure;

namespace Koan.Data.Connector.Json;

/// <summary>
/// Auto-registers the JSON adapter and health contributor during Koan initialization.
/// </summary>
// legacy initializer removed in favor of standardized auto-registrar

internal sealed class JsonDataOptionsConfigurator : IConfigureOptions<JsonDataOptions>
{
    private readonly IConfiguration? _config;
    // Prefer IConfiguration when available; do not fail if it's missing (non-host apps)
    public JsonDataOptionsConfigurator() { }
    public JsonDataOptionsConfigurator(IConfiguration config) { _config = config; }
    public void Configure(JsonDataOptions options)
    {
        // ADR-0040: avoid Bind; read via helper with centralized keys
        // Prefer explicit DirectoryPath if provided in either section, otherwise keep default
        var dir = Koan.Core.Configuration.ReadFirst(_config, new[]
        {
            $"{Constants.Configuration.Section_Data}:{Constants.Configuration.Keys.DirectoryPath}",
            $"{Constants.Configuration.Section_Sources_Default}:{Constants.Configuration.Keys.DirectoryPath}"
        });
        if (!string.IsNullOrWhiteSpace(dir))
            options.DirectoryPath = dir!;
    }
}
