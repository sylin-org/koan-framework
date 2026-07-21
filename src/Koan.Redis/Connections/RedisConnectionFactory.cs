using Koan.Core;
using Koan.Core.Logging;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Koan.Redis.Connections;

internal static class RedisConnectionFactory
{
    internal static IConnectionMultiplexer Connect(string connectionString, ILogger? logger = null)
    {
        var abortConnectImplicitlyDefaulted =
            !connectionString.Contains("abortConnect", StringComparison.OrdinalIgnoreCase);

        try
        {
            var options = ConfigurationOptions.Parse(connectionString);
            if (abortConnectImplicitlyDefaulted)
                options.AbortOnConnectFail = false;

            var multiplexer = ConnectionMultiplexer.Connect(options);
            if (abortConnectImplicitlyDefaulted &&
                !multiplexer.IsConnected &&
                !IsDeliberatelyUnreachable(options))
            {
                KoanLog.BootWarning(
                    logger,
                    Infrastructure.Constants.Logging.Connection,
                    "disconnected",
                    ("connection", Redaction.DeIdentify(connectionString)),
                    ("abortOnConnectFail", false),
                    ("guidance", "The host remains available, but Redis operations will fail until this endpoint is reachable. Pin abortConnect=true to fail fast."));
            }

            return multiplexer;
        }
        catch (RedisConnectionException ex)
        {
            KoanLog.BootError(logger, Infrastructure.Constants.Logging.Connection, "failed", ("error", ex));
            throw new InvalidOperationException(
                $"Redis is not available. Connection string: {Redaction.DeIdentify(connectionString)}. " +
                "Ensure Redis is running or use the Aspire AppHost for managed Redis.",
                ex);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            KoanLog.BootError(logger, Infrastructure.Constants.Logging.Connection, "malformed", ("error", ex));
            throw new InvalidOperationException(
                $"Redis connection string is malformed and could not be parsed: {Redaction.DeIdentify(connectionString)}. " +
                "Expected StackExchange.Redis configuration syntax (for example, 'localhost:6379').",
                ex);
        }
    }

    private static bool IsDeliberatelyUnreachable(ConfigurationOptions options)
    {
        foreach (var endpoint in options.EndPoints)
        {
            switch (endpoint)
            {
                case System.Net.DnsEndPoint dns when dns.Port == 0:
                case System.Net.IPEndPoint ip when ip.Port == 0:
                    return true;
            }
        }

        return false;
    }
}
