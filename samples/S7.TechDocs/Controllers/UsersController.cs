using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using S7.TechDocs.Services;
using S7.TechDocs.Models;

namespace S7.TechDocs.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "Admin")]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;

    public UsersController(IUserService userService)
    {
        _userService = userService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var users = await _userService.GetAllAsync();
        return Ok(users);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var user = await _userService.GetByIdAsync(id);
        if (user == null)
        {
            return NotFound();
        }
        return Ok(user);
    }

    [HttpPatch("{id}/roles")]
    public async Task<IActionResult> UpdateRoles(string id, [FromBody] UpdateRolesRequest request)
    {
        var updated = await _userService.UpdateRolesAsync(id, request.Roles);
        return Ok(updated);
    }
}

public class UpdateRolesRequest
{
    public List<string> Roles { get; set; } = new();
}
