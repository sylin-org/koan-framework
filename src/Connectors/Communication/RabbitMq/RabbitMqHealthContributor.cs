using Koan.Communication.Adapters;
using Koan.Communication.Connector.RabbitMq.Infrastructure;
using Koan.Core;
using Koan.Core.Observability.Health;

namespace Koan.Communication.Connector.RabbitMq;

internal sealed class RabbitMqHealthContributor : IHealthContributor
{
    private readonly RabbitMqCommunicationAdapter _adapter;

    public RabbitMqHealthContributor(IEnumerable<ICommunicationAdapter> adapters)
        => _adapter = adapters.OfType<RabbitMqCommunicationAdapter>().Single();

    public string Name => Constants.Health.Name;
    public bool IsCritical => _adapter.IsActivated;

    public Task<HealthReport> Check(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!_adapter.IsActivated)
        {
            return Task.FromResult(new HealthReport(
                Name,
                HealthState.Healthy,
                "RabbitMQ is available but not elected",
                null,
                new Dictionary<string, object?>
                {
                    ["active"] = false,
                    ["provider"] = Constants.ProviderId
                }));
        }

        return Task.FromResult(new HealthReport(
            Name,
            _adapter.IsReady ? HealthState.Healthy : HealthState.Unhealthy,
            _adapter.IsReady ? null : "The elected RabbitMQ Communication provider is unavailable",
            null,
            new Dictionary<string, object?>
            {
                ["active"] = true,
                ["provider"] = Constants.ProviderId,
                ["ready"] = _adapter.IsReady,
                ["error"] = _adapter.LastFailure
            }));
    }
}
