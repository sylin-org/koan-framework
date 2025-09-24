using Microsoft.AspNetCore.Mvc;
using Koan.Web.Controllers;
using S10.DevPortal.Models;

namespace S10.DevPortal.Controllers;

/// <summary>
/// Basic user management with zero boilerplate
/// </summary>
[Route("api/[controller]")]
public class UsersController : EntityController<User>
{
    // Inherits all CRUD operations automatically
    // Demonstrates that even the simplest controller gets full functionality
}