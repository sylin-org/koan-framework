using Koan.Core.Logging;
using Koan.Data.Vector;
using Koan.Data.Vector.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Koan.Data.Vector.Connector.RedisVector;

internal sealed class RedisVectorHealthContributor(
    RedisVectorVectorAdapterFactory factory,
    IVectorAdapterParticipation participation,
    ILogger<RedisVectorHealthContributor>? logger = null)
    : VectorAdapterHealthContributorBase(Infrastructure.Constants.Provider.Name, participation)
{
    protected override async Task ProbeSource(string source, CancellationToken ct)
    {
        var route = factory.ResolveRoute(source);
        try
        {
            _ = await route.Data.ExecuteAsync(
                    Infrastructure.Constants.Commands.Info,
                    Infrastructure.Constants.Wire.HealthProbeIndex)
                .WaitAsync(ct)
                .ConfigureAwait(false);
        }
        catch (RedisServerException error) when (
            error.Message.Contains("Unknown Index name", StringComparison.OrdinalIgnoreCase) ||
            error.Message.Contains("no such index", StringComparison.OrdinalIgnoreCase))
        {
            // The expected missing-index response proves the low-privilege Redis Search command surface.
        }
        catch (RedisServerException error) when (error.Message.Contains("unknown command", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "RedisVector requires Redis Search with vector support; plain Redis is not sufficient.", error);
        }
        KoanLog.HealthDebug(logger, Infrastructure.Constants.HealthLog, "healthy", ("source", source));
    }
}
