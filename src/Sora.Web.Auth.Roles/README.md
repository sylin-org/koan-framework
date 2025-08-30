# Sora.Web.Auth.Roles

First-class role and permission attribution for ASP.NET Core applications. Enriches `ClaimsPrincipal` with normalized roles and permissions, enabling `[Authorize]` to work with roles or canonical capability policies without custom authorization handlers.

## Quick Start

Register the module and it works with zero configuration:

```csharp
// Program.cs
builder.Services.AddSora()
    .AddSoraWebAuthRoles();
```

Now controllers can use roles directly:

```csharp
[Authorize(Roles = "admin")]
public class AdminController : ControllerBase
{
    [HttpGet]
    public IActionResult Dashboard() => Ok("Admin dashboard");
}
```

## Usage Scenarios

### Scenario 1: Default Configuration

**Assumption**: Your identity provider emits roles via standard claim types (`roles`, `role`, `groups`, or `ClaimTypes.Role`).

```csharp
// Program.cs - no additional configuration needed
builder.Services.AddSora()
    .AddSoraWebAuthRoles();

// Controllers work immediately with standard role names
[Authorize(Roles = "admin")]
public class AdminController : ControllerBase
{
    [HttpGet("users")]
    public IActionResult GetUsers() => Ok("User list");
    
    [HttpPost("users/{id}/suspend")]
    public IActionResult SuspendUser(string id) => Ok($"User {id} suspended");
}

[Authorize(Roles = "moderator")]
public class ModerationController : ControllerBase
{
    [HttpGet("reports")]
    public IActionResult GetReports() => Ok("Reports list");
    
    // Built-in aliases work automatically (administrator → admin)
    [Authorize(Roles = "admin,moderator")]
    [HttpPost("reports/{id}/resolve")]
    public IActionResult ResolveReport(int id) => Ok("Report resolved");
}

// No authorization required - public endpoint
public class PublicController : ControllerBase
{
    [HttpGet("articles")]
    public IActionResult GetArticles() => Ok("Public articles");
    
    // Development fallback gives 'reader' role when no roles present
    [Authorize(Roles = "reader")]
    [HttpGet("drafts")]
    public IActionResult GetDrafts() => Ok("Draft articles");
}
```

**What happens**: Claims transformer extracts roles from incoming claims, applies built-in aliases (`administrator` → `admin`, `viewer` → `reader`), and adds them as `ClaimTypes.Role` claims. Development fallback adds `reader` role when no roles found.

### Scenario 2: Custom Claim Keys

**Need**: Your provider uses custom claim keys like `user_roles` or `permissions`.

```json
{
  "Sora": {
    "Web": {
      "Auth": {
        "Roles": {
          "ClaimKeys": {
            "Roles": ["user_roles", "groups"],
            "Permissions": ["user_perms", "scopes"]
          }
        }
      }
    }
  }
}
```

Controllers use standard authorization patterns regardless of custom claim keys:

```csharp
public class BlogController : ControllerBase
{
    // Works with custom 'user_roles' claim
    [Authorize(Roles = "content-author")]
    [HttpPost("posts")]
    public IActionResult CreatePost([FromBody] BlogPost post) => Ok("Post created");
    
    [Authorize(Roles = "content-editor")]
    [HttpPut("posts/{id}")]
    public IActionResult EditPost(int id, [FromBody] BlogPost post) => Ok("Post updated");
    
    // Multiple roles from custom claims
    [Authorize(Roles = "content-editor,admin")]
    [HttpDelete("posts/{id}")]
    public IActionResult DeletePost(int id) => Ok("Post deleted");
}

public class ApiController : ControllerBase
{
    // Permissions extracted from 'user_perms' or 'scopes' claims
    [Authorize(Roles = "api-consumer")]
    [HttpGet("data")]
    public IActionResult GetData() => Ok("API data");
    
    [Authorize(Roles = "api-publisher")]
    [HttpPost("data")]
    public IActionResult PostData([FromBody] object data) => Ok("Data published");
}
```

### Scenario 3: Custom Aliases and Bootstrap

**Need**: Map provider-specific role names to your canonical roles, plus auto-elevate first admin.

```json
{
  "Sora": {
    "Web": {
      "Auth": {
        "Roles": {
          "Aliases": {
            "Map": {
              "super-user": "admin",
              "content-creator": "author",
              "read-only": "reader"
            }
          },
          "Bootstrap": {
            "Mode": "FirstUser"
          }
        }
      }
    }
  }
}
```

Controllers use your canonical role names regardless of what the identity provider sends:

```csharp
[Authorize(Roles = "admin")]
public class SystemController : ControllerBase
{
    [HttpGet("settings")]
    public IActionResult GetSettings() => Ok("System settings");
    
    [HttpPost("maintenance")]
    public IActionResult StartMaintenance() => Ok("Maintenance started");
}

[Authorize(Roles = "author")]
public class ContentController : ControllerBase
{
    [HttpPost("articles")]
    public IActionResult CreateArticle() => Ok("Article created");
    
    // Multiple roles
    [Authorize(Roles = "author,admin")]
    [HttpDelete("articles/{id}")]
    public IActionResult DeleteArticle(int id) => Ok("Article deleted");
}

[Authorize(Roles = "reader")]
public class PublicController : ControllerBase
{
    [HttpGet("articles")]
    public IActionResult GetArticles() => Ok("Article list");
}
```

**Result**: First authenticated user gets `admin` role automatically. Provider roles like `super-user` become `admin` in your application, enabling clean authorization logic.

### Scenario 4: Seeded Roles with Import

**Need**: Define roles in configuration and import them into the database.

```json
{
  "Sora": {
    "Web": {
      "Auth": {
        "Roles": {
          "Roles": [
            { "Id": "admin", "Display": "Administrator", "Description": "Full system access" },
            { "Id": "moderator", "Display": "Moderator", "Description": "Content moderation" },
            { "Id": "author", "Display": "Author", "Description": "Content creation" },
            { "Id": "subscriber", "Display": "Subscriber", "Description": "Premium content access" }
          ],
          "PolicyBindings": [
            { "Id": "auth.roles.admin", "Requirement": "role:admin" },
            { "Id": "content.moderate", "Requirement": "role:moderator,admin" },
            { "Id": "content.publish", "Requirement": "role:author,moderator,admin" },
            { "Id": "premium.access", "Requirement": "role:subscriber,admin" }
          ]
        }
      }
    }
  }
}
```

Controllers can use both role-based and policy-based authorization:

```csharp
[Authorize(Roles = "admin")]
public class AdminController : ControllerBase
{
    [HttpGet("dashboard")]
    public IActionResult Dashboard() => Ok("Admin dashboard");
    
    [HttpPost("users/{id}/ban")]
    public IActionResult BanUser(string id) => Ok($"User {id} banned");
}

public class ContentController : ControllerBase
{
    // Use policy for more flexible authorization
    [Authorize(Policy = "content.publish")]
    [HttpPost("articles")]
    public IActionResult PublishArticle([FromBody] Article article) => Ok("Article published");
    
    [Authorize(Policy = "content.moderate")]
    [HttpPost("articles/{id}/approve")]
    public IActionResult ApproveArticle(int id) => Ok("Article approved");
    
    // Role-based for simpler cases
    [Authorize(Roles = "author")]
    [HttpPost("drafts")]
    public IActionResult SaveDraft([FromBody] Article draft) => Ok("Draft saved");
}

public class PremiumController : ControllerBase
{
    [Authorize(Policy = "premium.access")]
    [HttpGet("exclusive-content")]
    public IActionResult GetExclusiveContent() => Ok("Premium content");
    
    // Multiple authorization approaches
    [Authorize(Roles = "subscriber")]
    [HttpGet("subscriber-only")]
    public IActionResult GetSubscriberContent() => Ok("Subscriber content");
}
```

Import the configuration:

```csharp
// Import via admin API (authenticated admin required)
POST /api/auth/roles/import?dryRun=false&force=true

// Or programmatically
var controller = serviceProvider.GetService<RolesAdminController>();
await controller.Import(dryRun: false, force: true);
```

**Result**: Database is populated with roles and policy bindings. Controllers can use either `[Authorize(Roles = "...")]` for simple cases or `[Authorize(Policy = "...")]` for more complex requirements like multiple role options.

### Scenario 5: Policy-Based Authorization

**Need**: Use capability-based policies instead of role checks.

```csharp
// Enable default policy bindings
builder.Services.AddSoraWebAuthRoles()
    .AddSoraRolePolicies(); // Maps roles to capability policies

// Controllers demonstrate different authorization patterns
public class ModerationController : ControllerBase
{
    // Policy-based: allows multiple roles
    [Authorize(Policy = "content.moderate")]
    [HttpPost("posts/{id}/moderate")]
    public IActionResult ModeratePost(int id) => Ok("Post moderated");
    
    // Role-based: specific role only  
    [Authorize(Roles = "admin")]
    [HttpDelete("posts/{id}")]
    public IActionResult DeletePost(int id) => Ok("Post deleted");
    
    // Mixed: different actions, different requirements
    [Authorize(Policy = "content.publish")]
    [HttpPost("posts/{id}/publish")]
    public IActionResult PublishPost(int id) => Ok("Post published");
}
```

### Scenario 6: Custom Role Contributors

**Need**: Add roles from external systems or complex business logic.

```csharp
public class TeamRoleContributor : IRoleMapContributor
{
    private readonly ITeamService _teamService;
    
    public TeamRoleContributor(ITeamService teamService)
    {
        _teamService = teamService;
    }
    
    public async Task ContributeAsync(ClaimsPrincipal user, ISet<string> roles, ISet<string> permissions, RoleAttributionContext? context, CancellationToken ct)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userId != null)
        {
            // Query team membership from external API
            var teams = await _teamService.GetUserTeams(userId, ct);
            foreach (var team in teams)
            {
                if (team.Role == "lead") roles.Add("team-lead");
                if (team.Role == "member") roles.Add("team-member");
                if (team.Department == "Engineering") roles.Add("engineer");
            }
        }
    }
}

// Register contributor
builder.Services.AddSingleton<IRoleMapContributor, TeamRoleContributor>();
```

Controllers can now use the contributed roles:

```csharp
public class ProjectController : ControllerBase
{
    // Uses role contributed by TeamRoleContributor
    [Authorize(Roles = "team-lead")]
    [HttpPost("projects")]
    public IActionResult CreateProject([FromBody] Project project) => Ok("Project created");
    
    [Authorize(Roles = "team-member")]
    [HttpGet("projects/{id}/tasks")]
    public IActionResult GetTasks(int id) => Ok("Task list");
    
    // Combine contributed and standard roles
    [Authorize(Roles = "engineer,admin")]
    [HttpPost("projects/{id}/deploy")]
    public IActionResult DeployProject(int id) => Ok("Project deployed");
}

public class TeamController : ControllerBase
{
    [Authorize(Roles = "team-lead")]
    [HttpGet("team/members")]
    public IActionResult GetTeamMembers() => Ok("Team members");
    
    [Authorize(Roles = "team-lead,admin")]
    [HttpPost("team/members/{id}/promote")]
    public IActionResult PromoteMember(string id) => Ok("Member promoted");
    
    // Any team member can view team info
    [Authorize(Roles = "team-member")]
    [HttpGet("team/info")]
    public IActionResult GetTeamInfo() => Ok("Team information");
}
```

### Scenario 7: Production Bootstrap with Email List

**Need**: Auto-elevate specific users to admin in production.

```json
{
  "Sora": {
    "Web": {
      "Auth": {
        "Roles": {
          "Bootstrap": {
            "Mode": "ClaimMatch",
            "AdminEmails": ["admin@company.com", "cto@company.com"]
          },
          "AllowSeedingInProduction": true
        }
      }
    }
  }
}
```

Controllers work normally, with bootstrap users automatically getting admin access:

```csharp
[Authorize(Roles = "admin")]
public class SystemAdminController : ControllerBase
{
    [HttpGet("system/health")]
    public IActionResult GetSystemHealth() => Ok("System healthy");
    
    [HttpPost("system/maintenance")]
    public IActionResult EnableMaintenance() => Ok("Maintenance mode enabled");
    
    [HttpGet("users/audit")]
    public IActionResult GetUserAudit() => Ok("User audit log");
}

public class ConfigurationController : ControllerBase
{
    // Only bootstrap admins can access initially
    [Authorize(Roles = "admin")]
    [HttpGet("config")]
    public IActionResult GetConfiguration() => Ok("System configuration");
    
    [Authorize(Roles = "admin")]
    [HttpPut("config")]
    public IActionResult UpdateConfiguration([FromBody] object config) => Ok("Configuration updated");
    
    // Once other admins are created, they can also access
    [Authorize(Roles = "admin")]
    [HttpPost("admin/create")]
    public IActionResult CreateAdmin([FromBody] CreateAdminRequest request) => Ok("Admin created");
}

// Regular controllers continue working as normal
[Authorize(Roles = "user")]
public class UserController : ControllerBase
{
    [HttpGet("profile")]
    public IActionResult GetProfile() => Ok("User profile");
    
    [HttpPut("profile")]
    public IActionResult UpdateProfile([FromBody] object profile) => Ok("Profile updated");
}
```

**Result**: Users with emails `admin@company.com` or `cto@company.com` automatically get `admin` role on first login, enabling initial system setup. Subsequent users follow normal role assignment.

## Recommended Usage Patterns

### Individual Developers / Prototypes

**Pattern**: Start simple, leverage defaults
- Use zero-configuration setup with built-in aliases
- Rely on development fallback for quick testing
- Use FirstUser bootstrap for initial admin access

```csharp
// Minimal setup
builder.Services.AddSora()
    .AddSoraWebAuthRoles();
```

```json
{
  "Sora": {
    "Web": {
      "Auth": {
        "Roles": {
          "Bootstrap": {
            "Mode": "FirstUser"
          }
        }
      }
    }
  }
}
```

**Controllers**: Keep authorization simple
```csharp
[Authorize(Roles = "admin")]
public class AdminController : ControllerBase { }

[Authorize(Roles = "user")]  // or rely on dev fallback "reader"
public class UserController : ControllerBase { }
```

**Benefits**: Fast setup, no external dependencies, works immediately with common identity providers.

### Small Teams (2-10 developers)

**Pattern**: Configuration-driven with team-specific roles
- Define roles in appsettings for consistency
- Use email-based bootstrap for known team leads
- Implement custom aliases for external provider mapping

```json
{
  "Sora": {
    "Web": {
      "Auth": {
        "Roles": {
          "Roles": [
            { "Id": "owner", "Display": "Project Owner" },
            { "Id": "dev", "Display": "Developer" },
            { "Id": "qa", "Display": "QA Tester" }
          ],
          "Aliases": {
            "Map": {
              "developer": "dev",
              "tester": "qa",
              "lead": "owner"
            }
          },
          "Bootstrap": {
            "Mode": "ClaimMatch",
            "AdminEmails": ["lead@team.com", "owner@team.com"]
          }
        }
      }
    }
  }
}
```

**Controllers**: Role-based with clear team responsibilities
```csharp
public class ProjectController : ControllerBase
{
    [Authorize(Roles = "owner")]
    [HttpPost("deploy")]
    public IActionResult Deploy() => Ok();
    
    [Authorize(Roles = "dev,owner")]
    [HttpPost("commits")]
    public IActionResult Commit() => Ok();
    
    [Authorize(Roles = "qa,owner")]
    [HttpPost("test-results")]
    public IActionResult SubmitTestResults() => Ok();
}
```

**Import Strategy**: Use configuration import for environment consistency
```bash
# Deploy same roles across dev/staging/prod
POST /api/auth/roles/import?force=true
```

**Benefits**: Clear role separation, easy onboarding, consistent across environments.

### Corporate Teams / Enterprise

**Pattern**: Policy-driven with fine-grained permissions
- Use policy bindings for complex authorization scenarios
- Implement custom contributors for integration with corporate systems (AD, LDAP, HR systems)
- Separate role management from application deployment

```csharp
// Corporate integration
public class ActiveDirectoryRoleContributor : IRoleMapContributor
{
    public async Task ContributeAsync(ClaimsPrincipal user, ISet<string> roles, ISet<string> permissions, RoleAttributionContext? context, CancellationToken ct)
    {
        var groups = user.FindAll("groups").Select(c => c.Value);
        
        // Map AD groups to application roles
        if (groups.Contains("IT-Admins")) roles.Add("system-admin");
        if (groups.Contains("Security-Team")) roles.Add("security-officer");
        if (groups.Contains("Managers")) roles.Add("manager");
        if (groups.Contains("Employees")) roles.Add("employee");
        
        // Department-specific roles
        if (groups.Contains("Engineering")) roles.Add("engineer");
        if (groups.Contains("Legal")) roles.Add("legal-counsel");
    }
}

// Register enterprise services
builder.Services.AddSoraWebAuthRoles()
    .AddSoraRolePolicies()
    .AddSingleton<IRoleMapContributor, ActiveDirectoryRoleContributor>();
```

**Policy-First Authorization**:
```json
{
  "Sora": {
    "Web": {
      "Auth": {
        "Roles": {
          "PolicyBindings": [
            { "Id": "data.read", "Requirement": "role:employee,contractor" },
            { "Id": "data.write", "Requirement": "role:engineer,manager" },
            { "Id": "data.delete", "Requirement": "role:system-admin" },
            { "Id": "audit.view", "Requirement": "role:security-officer,system-admin" },
            { "Id": "user.manage", "Requirement": "role:manager,system-admin" },
            { "Id": "system.configure", "Requirement": "role:system-admin" }
          ],
          "Bootstrap": {
            "Mode": "ClaimMatch",
            "ClaimType": "groups",
            "ClaimValues": ["IT-Admins"]
          },
          "AllowSeedingInProduction": false
        }
      }
    }
  }
}
```

**Controllers**: Policy-based with business capability focus
```csharp
public class DataController : ControllerBase
{
    [Authorize(Policy = "data.read")]
    [HttpGet("reports")]
    public IActionResult GetReports() => Ok();
    
    [Authorize(Policy = "data.write")]
    [HttpPost("reports")]
    public IActionResult CreateReport() => Ok();
    
    [Authorize(Policy = "data.delete")]
    [HttpDelete("reports/{id}")]
    public IActionResult DeleteReport(int id) => Ok();
}

public class AdminController : ControllerBase
{
    [Authorize(Policy = "user.manage")]
    [HttpGet("users")]
    public IActionResult GetUsers() => Ok();
    
    [Authorize(Policy = "audit.view")]
    [HttpGet("audit-log")]
    public IActionResult GetAuditLog() => Ok();
    
    [Authorize(Policy = "system.configure")]
    [HttpPut("settings")]
    public IActionResult UpdateSettings() => Ok();
}
```

**Enterprise Management**:
- Role definitions managed separately from code
- Database-driven role/policy management via Admin API
- Integration with corporate identity systems
- Audit trails and compliance reporting

```csharp
// Programmatic role management for enterprise workflows
public class CorporateRoleService
{
    private readonly RolesAdminController _rolesAdmin;
    
    public async Task SyncWithHRSystem()
    {
        // Sync roles based on HR data, org chart changes
        var hrRoles = await _hrService.GetCurrentRoles();
        await _rolesAdmin.Import(dryRun: false, force: true);
    }
    
    public async Task OnboardNewEmployee(string email, string department)
    {
        // Automatic role assignment based on department
        var departmentRoles = _departmentRoleMapping[department];
        // ... role assignment logic
    }
}
```

**Benefits**: Scales to large organizations, integrates with existing systems, supports compliance requirements, clear separation of concerns.

## Admin API

Access role management via REST API (requires `auth.roles.admin` policy):

```bash
# List roles
GET /api/auth/roles

# Create/update role
PUT /api/auth/roles/admin
{
  "display": "Administrator", 
  "description": "Full access"
}

# List aliases
GET /api/auth/roles/aliases

# Import from configuration
POST /api/auth/roles/import?dryRun=true&force=true

# Reload snapshot (refreshes cache)
POST /api/auth/roles/reload
```

## Development Features

- **Dev Fallback**: Automatically adds `reader` role in Development when user has no roles
- **Bootstrap Modes**: `FirstUser` or `ClaimMatch` for initial admin setup
- **Import/Export**: Sync roles between environments via configuration templates
- **Cache Invalidation**: Attribution results cached per user; cleared on admin changes

## Data Models

Uses first-class Entity statics for clean data access:

```csharp
// Query roles
var roles = await Role.All(ct);
var adminRole = await Role.Get("admin", ct);

// Create/update
var newRole = new Role { Id = "editor", Display = "Editor" };
await Role.UpsertMany(new[] { newRole }, ct);

// Aliases and policy bindings work the same way
var aliases = await RoleAlias.All(ct);
var bindings = await RolePolicyBinding.All(ct);
```
