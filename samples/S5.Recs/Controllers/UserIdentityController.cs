using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace S5.Recs.Controllers;

/// <summary>
/// Provides user identity endpoints with graceful handling of unauthenticated requests.
/// </summary>
[ApiController]
public class UserIdentityController : ControllerBase
{
    /// <summary>
    /// Returns current user information or indicates unauthenticated state.
    /// Maps to /me to match frontend expectations.
    /// </summary>
    [HttpGet("me")]
    public IActionResult Me()
    {
        // Check if user is authenticated
        if (User?.Identity?.IsAuthenticated != true)
        {
            // Return 401 Unauthorized instead of trying to challenge
            // This prevents the InvalidOperationException from the missing challenge scheme
            return Unauthorized(new {
                error = "not_authenticated",
                message = "User is not authenticated",
                loginUrl = "/.testoauth/login.html" // Provide login URL for frontend
            });
        }

        // Extract user information from claims
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = User.FindFirst(ClaimTypes.Email)?.Value;
        var name = User.FindFirst(ClaimTypes.Name)?.Value;
        var username = User.FindFirst("preferred_username")?.Value ??
                      User.FindFirst("username")?.Value ??
                      email?.Split('@')[0];

        // Get roles and other claims
        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
        var claims = User.Claims
            .Where(c => !new[] { ClaimTypes.NameIdentifier, ClaimTypes.Email, ClaimTypes.Name, ClaimTypes.Role }.Contains(c.Type))
            .GroupBy(c => c.Type)
            .ToDictionary(g => g.Key, g => g.Select(c => c.Value).ToArray());

        return Ok(new
        {
            id = userId ?? email,
            username = username ?? "user",
            email = email ?? "unknown@local",
            authenticated = true,
            roles,
            claims = claims.Any() ? claims : null
        });
    }
}