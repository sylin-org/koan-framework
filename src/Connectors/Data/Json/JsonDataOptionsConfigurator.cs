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

        // Optional paging guardrails
        var dps = Koan.Core.Configuration.ReadFirst(_config, new[]
        {
            $"{Constants.Configuration.Section_Data}:{Constants.Configuration.Keys.DefaultPageSize}",
            $"{Constants.Configuration.Section_Sources_Default}:{Constants.Configuration.Keys.DefaultPageSize}"
        });
        if (int.TryParse(dps, out var dpsVal) && dpsVal > 0) options.DefaultPageSize = dpsVal;
        var mps = Koan.Core.Configuration.ReadFirst(_config, new[]
        {
            $"{Constants.Configuration.Section_Data}:{Constants.Configuration.Keys.MaxPageSize}",
            $"{Constants.Configuration.Section_Sources_Default}:{Constants.Configuration.Keys.MaxPageSize}"
        });
        if (int.TryParse(mps, out var mpsVal) && mpsVal > 0) options.MaxPageSize = mpsVal;
        if (options.DefaultPageSize > options.MaxPageSize)
            options.DefaultPageSize = options.MaxPageSize;
    }
}
