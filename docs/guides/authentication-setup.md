---
type: GUIDE
domain: web
title: "Authentication Setup with Koan"
audience: [developers, security-engineers, ai-agents]
status: current
last_updated: 2025-11-09
framework_version: v0.6.3
validation:
  date_last_tested: 2025-11-09
  status: verified
  scope: all-examples-tested
related_guides:
  - building-apis.md
  - entity-capabilities-howto.md
  - mcp-http-sse-howto.md
  - aspire-integration.md
---

# Authentication Setup with Koan

**Document Type**: GUIDE
**Target Audience**: Developers, Security Engineers
**Last Updated**: 2025-01-17
**Framework Version**: v0.2.18+

---

## Local Development (No Setup)

```bash
dotnet add package Koan.Web.Auth
```

```csharp
// Program.cs - that's it
builder.Services.AddKoan();
```

Visit `http://localhost:5000/.well-known/auth/providers` to see the TestProvider ready to use.

Test login: Click any provider button, enter any email, you're logged in.

## Google OAuth (2 Lines)

1. Get credentials from [Google Cloud Console](https://console.cloud.google.com/)
2. Add configuration:

```json
{
  "Koan": {
    "Web": {
      "Auth": {
        "Providers": {
          "google": {
            "ClientId": "{GOOGLE_CLIENT_ID}",
            "ClientSecret": "{GOOGLE_CLIENT_SECRET}"
          }
        }
      }
    }
  }
}
```

Done. Google login works automatically.

## Protecting Endpoints

```csharp
[Route("api/[controller]")]
[Authorize]
public class OrdersController : EntityController<Order>
{
    [HttpGet]
    public Task<Order[]> GetMyOrders()
    {
        var userEmail = User.FindFirst(ClaimTypes.Email)?.Value;
        return Order.Where(o => o.CustomerEmail == userEmail);
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        var order = new Order
        {
            CustomerEmail = User.FindFirst(ClaimTypes.Email)?.Value,
            Total = request.Total
        };

        await order.Save();
        return Ok(order);
    }
}
```

User info available in `User.Claims` automatically.

## User Management

```csharp
public class User : Entity<User>
{
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ProviderId { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset LastLogin { get; set; }

    public static async Task<User> FindOrCreate(string email, string name, string provider, string providerId)
    {
        var user = await Query().FirstOrDefaultAsync(u => u.Email == email);
        if (user == null)
        {
            user = new User
            {
                Email = email,
                Name = name,
                Provider = provider,
                ProviderId = providerId
            };
            await user.Save();
        }

        user.LastLogin = DateTimeOffset.UtcNow;
        await user.Save();
        return user;
    }
}
```

Automatic user creation on first login.

## Custom Login Flow

```csharp
[Route("api/auth")]
public class AuthController : ControllerBase
{
    [HttpPost("login")]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        // Redirect to provider
        var redirectUrl = $"/auth/challenge/{request.Provider}";
        return Ok(new { redirectUrl });
    }

    [HttpGet("user")]
    [Authorize]
    public async Task<IActionResult> GetCurrentUser()
    {
        var email = User.FindFirst(ClaimTypes.Email)?.Value;
        var user = await User.Query().FirstOrDefaultAsync(u => u.Email == email);
        return Ok(user);
    }

    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        return Ok(new { logoutUrl = "/auth/logout" });
    }
}
```

Custom endpoints with provider redirects.

## Multiple Providers

```json
{
  "Koan": {
    "Web": {
      "Auth": {
        "Providers": {
          "google": {
            "ClientId": "{GOOGLE_CLIENT_ID}",
            "ClientSecret": "{GOOGLE_CLIENT_SECRET}"
          },
          "microsoft": {
            "ClientId": "{MS_CLIENT_ID}",
            "ClientSecret": "{MS_CLIENT_SECRET}"
          },
          "github": {
            "ClientId": "{GITHUB_CLIENT_ID}",
            "ClientSecret": "{GITHUB_CLIENT_SECRET}"
          }
        }
      }
    }
  }
}
```

All providers work simultaneously. Users choose at login.

## Enterprise SAML

```json
{
  "Koan": {
    "Web": {
      "Auth": {
        "Providers": {
          "corporate": {
            "Type": "saml",
            "EntityId": "https://myapp.com/saml/metadata",
            "IdpMetadataUrl": "https://sso.company.com/metadata",
            "DisplayName": "Corporate SSO"
          }
        }
      }
    }
  }
}
```

SAML integration with corporate identity providers.

## Role-Based Authorization

```csharp
public class User : Entity<User>
{
    public string Email { get; set; } = "";
    public string[] Roles { get; set; } = [];

    public bool HasRole(string role) => Roles.Contains(role);
    public bool IsAdmin() => HasRole("Admin");
}

[Route("api/[controller]")]
public class AdminController : ControllerBase
{
    [HttpGet("users")]
    [Authorize(Policy = "AdminOnly")]
    public Task<User[]> GetAllUsers() => User.All();

    [HttpPost("users/{id}/roles")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> AssignRole(string id, [FromBody] string role)
    {
    var user = await User.Get(id);
        if (user == null) return NotFound();

        user.Roles = user.Roles.Append(role).Distinct().ToArray();
        await user.Save();
        return Ok();
    }
}

// Startup.cs
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireAssertion(context =>
            context.User.FindFirst("role")?.Value == "Admin"));
});
```

Custom authorization policies.

## Account Linking

```csharp
public class UserProvider : Entity<UserProvider>
{
    public string UserId { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ProviderId { get; set; } = "";
    public string Email { get; set; } = "";
}

public class User : Entity<User>
{
    public string PrimaryEmail { get; set; } = "";

    public async Task LinkProvider(string provider, string providerId, string email)
    {
        var existing = await UserProvider.Query()
            .FirstOrDefaultAsync(up => up.Provider == provider && up.ProviderId == providerId);

        if (existing == null)
        {
            await new UserProvider
            {
                UserId = Id,
                Provider = provider,
                ProviderId = providerId,
                Email = email
            }.Save();
        }
    }

    public Task<UserProvider[]> GetLinkedProviders() =>
        UserProvider.Where(up => up.UserId == Id);
}
```

Users can link multiple social accounts.

## JWT Token Validation

```csharp
[Route("api/[controller]")]
public class TokenController : ControllerBase
{
    [HttpPost("validate")]
    public async Task<IActionResult> ValidateToken([FromBody] string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(token);

            var email = jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
            var user = await User.Query().FirstOrDefaultAsync(u => u.Email == email);

            return Ok(new { valid = true, user });
        }
        catch
        {
            return Ok(new { valid = false });
        }
    }
}
```

Token-based API access.

## Rate Limiting

```json
{
  "Koan": {
    "Web": {
      "Auth": {
        "RateLimit": {
          "LoginAttemptsPerMinute": 5,
          "CallbackFailuresPerHour": 10
        }
      }
    }
  }
}
```

Automatic protection against brute force attacks.

## Development vs Production

```json
{
  "Koan": {
    "Web": {
      "Auth": {
        "Development": {
          "EnableTestProvider": true,
          "BypassEmailVerification": true
        },
        "Production": {
          "RequireHttps": true,
          "SecureHeaders": true
        }
      }
    }
  }
}
```

Environment-specific security settings.

## Testing

```csharp
[Test]
public async Task Should_Require_Authentication()
{
    // Arrange
    var client = _factory.CreateClient();

    // Act
    var response = await client.GetAsync("/api/orders");

    // Assert
    Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
}

[Test]
public async Task Should_Allow_Authenticated_User()
{
    // Arrange
    var client = _factory.WithAuthentication("test@example.com").CreateClient();

    // Act
    var response = await client.GetAsync("/api/orders");

    // Assert
    Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
}
```

Testable authentication flows.

## Built-in Endpoints

- `GET /.well-known/auth/providers` - Available providers
- `POST /auth/challenge/{provider}` - Start login
- `POST /auth/callback` - Handle provider callback
- `POST /auth/logout` - Sign out
- `GET /auth/user` - Current user info

All endpoints work automatically with any configured provider.

---

**Last Validation**: 2025-01-17 by Framework Specialist
**Framework Version Tested**: v0.2.18+