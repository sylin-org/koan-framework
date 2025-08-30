using Microsoft.AspNetCore.Mvc;
using S7.TechDocs.Services;

namespace S7.TechDocs.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CollectionsController : ControllerBase
{
    private readonly ICollectionService _collectionService;

    public CollectionsController(ICollectionService collectionService)
    {
        _collectionService = collectionService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var collections = await _collectionService.GetAllAsync();
        return Ok(collections);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var collection = await _collectionService.GetByIdAsync(id);
        if (collection == null)
        {
            return NotFound();
        }
        return Ok(collection);
    }
}
