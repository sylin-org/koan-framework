using Sora.Core.Observability.Health;

namespace Sora.Core;

public interface IHealthContributor
{
    string Name { get; }
    bool IsCritical { get; }
    Task<HealthReport> CheckAsync(CancellationToken ct = default);
}