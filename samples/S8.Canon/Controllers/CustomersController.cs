using Koan.Canon.Domain.Runtime;
using Koan.Canon.Web.Catalog;
using Koan.Canon.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using S8.Canon.Domain;

namespace S8.Canon.Controllers;

/// <summary>
/// Customer canonization API demonstrating Canon runtime pipeline processing.
/// Inherits from CanonEntitiesController to get automatic CRUD and canonization endpoints.
/// </summary>
/// <remarks>
/// Auto-generated endpoints:
/// - POST /api/canon/customers - Canonize customer data (runs validation → enrichment pipeline)
/// - GET /api/canon/customers - List canonical customers
/// - GET /api/canon/customers/{id} - Get canonical customer by ID
/// - DELETE /api/canon/customers/{id} - Remove canonical customer
/// - POST /api/canon/customers/{id}/rebuild - Rebuild customer views
/// </remarks>
[Route("api/canon/[controller]")]
public class CustomersController : CanonEntitiesController<Customer>
{
    public CustomersController(ICanonRuntime runtime, ICanonModelCatalog catalog)
        : base(runtime, catalog)
    {
    }

    // All CRUD + canonization endpoints are inherited from CanonEntitiesController<T>
    // Custom endpoints can be added here if needed

    /// <summary>
    /// Sample custom endpoint demonstrating how to extend the base controller.
    /// Gets customers by account tier.
    /// </summary>
    [HttpGet("by-tier/{tier}")]
    public async Task<IActionResult> GetByTier(string tier, CancellationToken cancellationToken = default)
    {
        var customers = await Customer.All(cancellationToken);
        var filtered = customers.Where(c => c.AccountTier.Equals(tier, StringComparison.OrdinalIgnoreCase));
        return Ok(filtered);
    }
}
