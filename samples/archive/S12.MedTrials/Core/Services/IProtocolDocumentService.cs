using System.Threading;
using System.Threading.Tasks;
using S12.MedTrials.Contracts;

namespace S12.MedTrials.Services;

public interface IProtocolDocumentService
{
    Task<ProtocolDocumentIngestionResult> IngestAsync(ProtocolDocumentIngestionRequest request, CancellationToken ct);
    Task<ProtocolDocumentQueryResult> QueryAsync(ProtocolDocumentQueryRequest request, CancellationToken ct);
}
