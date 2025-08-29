using System.Security.Claims;

namespace S7.TechDocs.Infrastructure;

/// <summary>
/// Development-only middleware to simulate TestProvider authentication
/// </summary>
public class TestAuthMiddleware
{
    private readonly RequestDelegate _next;

    public TestAuthMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // For demo purposes, auto-authenticate based on query parameter or default
        var roleParam = context.Request.Query["role"].FirstOrDefault();
        var userRole = roleParam ?? Constants.Roles.Author; // Default to Author for demo

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, GetUserName(userRole)),
            new(ClaimTypes.Email, GetUserEmail(userRole)),
            new(ClaimTypes.Role, userRole),
            new(ClaimTypes.NameIdentifier, GetUserId(userRole))
        };

        // Add cumulative roles (Reader ⊆ Author ⊆ Moderator ⊆ Admin)
        switch (userRole)
        {
            case Constants.Roles.Admin:
                claims.Add(new Claim(ClaimTypes.Role, Constants.Roles.Moderator));
                goto case Constants.Roles.Moderator;
            case Constants.Roles.Moderator:
                claims.Add(new Claim(ClaimTypes.Role, Constants.Roles.Author));
                goto case Constants.Roles.Author;
            case Constants.Roles.Author:
                claims.Add(new Claim(ClaimTypes.Role, Constants.Roles.Reader));
                break;
        }

        var identity = new ClaimsIdentity(claims, "TestProvider");
        context.User = new ClaimsPrincipal(identity);

        await _next(context);
    }

    private static string GetUserName(string role) => role switch
    {
        Constants.Roles.Admin => "Alex Admin",
        Constants.Roles.Moderator => "Maya Moderator", 
        Constants.Roles.Author => "Alice Author",
        _ => "Rob Reader"
    };

    private static string GetUserEmail(string role) => role switch
    {
        Constants.Roles.Admin => "alex@company.com",
        Constants.Roles.Moderator => "maya@company.com",
        Constants.Roles.Author => "alice@company.com", 
        _ => "rob@company.com"
    };

    private static string GetUserId(string role) => role switch
    {
        Constants.Roles.Admin => "admin-001",
        Constants.Roles.Moderator => "mod-001",
        Constants.Roles.Author => "auth-001",
        _ => "read-001"
    };
}
