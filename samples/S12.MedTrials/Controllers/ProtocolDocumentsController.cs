using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Koan.Web.Controllers;
using S12.MedTrials.Contracts;
using S12.MedTrials.Models;
using S12.MedTrials.Services;

namespace S12.MedTrials.Controllers;

[Route("api/protocol-documents")]
public sealed class ProtocolDocumentsController : EntityController<ProtocolDocument>
{
    private readonly IProtocolDocumentService _documents;

    public ProtocolDocumentsController(IProtocolDocumentService documents)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
    }

    [HttpPost("ingest")]
    public async Task<ActionResult<ProtocolDocumentIngestionResult>> Ingest([FromBody] ProtocolDocumentIngestionRequest request, CancellationToken ct)
    {
        var result = await _documents.IngestAsync(request, ct).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpPost("search")]
    public async Task<ActionResult<ProtocolDocumentQueryResult>> Search([FromBody] ProtocolDocumentQueryRequest request, CancellationToken ct)
    {
        var result = await _documents.QueryAsync(request, ct).ConfigureAwait(false);
        return Ok(result);
    }
}
