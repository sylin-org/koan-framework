using System.Threading;
using System.Threading.Tasks;
using S12.MedTrials.Contracts;

namespace S12.MedTrials.Services;

public interface IVisitPlanningService
{
    Task<VisitPlanningResult> PlanAsync(VisitPlanningRequest request, CancellationToken ct);
}
