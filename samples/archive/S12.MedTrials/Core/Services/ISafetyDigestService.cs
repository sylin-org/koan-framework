using System.Threading;
using System.Threading.Tasks;
using S12.MedTrials.Contracts;

namespace S12.MedTrials.Services;

public interface ISafetyDigestService
{
    Task<SafetySummaryResult> Summarise(SafetySummaryRequest request, CancellationToken ct);
}
