using Koan.Core.Adapters;
using Koan.Data.Adapters.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Redis;

/// <summary>Binds Data-specific Redis settings; the shared Redis backend owns endpoint resolution.</summary>
internal sealed class RedisOptionsConfigurator : AdapterOptionsConfigurator<RedisOptions>
{
    protected override string ProviderName => "Redis";

    public RedisOptionsConfigurator(
        IConfiguration configuration,
        ILogger<RedisOptionsConfigurator> logger,
        IOptions<AdaptersReadinessOptions> readinessOptions)
        : base(configuration, logger, readinessOptions)
    {
    }

    protected override void ConfigureProviderSpecific(RedisOptions options)
    {
        options.Database = ReadProviderConfiguration(
            options.Database,
            Infrastructure.Constants.Configuration.Keys.Database,
            Infrastructure.Constants.Configuration.Keys.AltDatabase);
        LogConfiguration(
            LogLevel.Information,
            "final",
            ("database", options.Database));
    }
}
