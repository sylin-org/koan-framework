using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Koan.Samples.Meridian.Controllers;

/// <summary>
/// CRUD endpoints for DocumentStyle entity.
/// Provides management of document style classifications and their detection hints.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public sealed class DocumentStylesController : EntityController<DocumentStyle>
{
    private readonly ILogger<DocumentStylesController> _logger;

    public DocumentStylesController(ILogger<DocumentStylesController> logger)
    {
        _logger = logger;
    }

    // EntityController<T> provides:
    // GET /api/documentstyles - List all
    // GET /api/documentstyles/{id} - Get by ID
    // POST /api/documentstyles - Create
    // PUT /api/documentstyles/{id} - Update
    // DELETE /api/documentstyles/{id} - Delete
}
