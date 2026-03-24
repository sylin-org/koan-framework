using Koan.Core.Observability.Health;

namespace Koan.Core;

public interface IHealthContributor
{
    string Name { get; }
    bool IsCritical { get; }
    Task<HealthReport> Check(CancellationToken ct = default);
}