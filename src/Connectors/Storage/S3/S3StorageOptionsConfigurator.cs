using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core.Logging;
using Koan.Storage.Connector.S3.Infrastructure;

namespace Koan.Storage.Connector.S3;

/// <summary>
/// Configures S3 storage options from explicit configuration only.
///
/// Does NOT resolve AppIdentity, ZenGarden endpoints, or credentials here.
/// All of those are resolved lazily at first use by S3StorageProvider —
/// AppHost.Identity isn't populated yet at configure time.
/// </summary>
internal sealed class S3StorageOptionsConfigurator : IConfigureOptions<S3StorageOptions>
{
    private readonly IConfiguration _config;
    private readonly ILogger<S3StorageOptionsConfigurator>? _logger;

    public S3StorageOptionsConfigurator(
        IConfiguration config,
        ILogger<S3StorageOptionsConfigurator>? logger = null)
    {
        _config = config;
        _logger = logger;
    }

    public void Configure(S3StorageOptions options)
    {
        // Bind from config section — explicit settings only
        _config.GetSection(S3StorageConstants.Configuration.Section).Bind(options);

        if (!string.IsNullOrWhiteSpace(options.Endpoint))
            KoanLog.ConfigInfo(_logger, S3StorageConstants.Logging.Configuration, "explicit",
                ("endpoint", options.Endpoint));
        else
            KoanLog.ConfigDebug(_logger, S3StorageConstants.Logging.Configuration, "lazy");
    }
}
