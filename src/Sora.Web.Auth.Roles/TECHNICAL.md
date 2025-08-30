# Sora.Web.Auth.Roles - Technical Reference

## Architecture

### Core Components

**IRoleAttributionService**: Primary service that computes roles and permissions for a `ClaimsPrincipal`. Called by `SoraRoleClaimsTransformation` to enrich claims during authentication pipeline.

**IRoleMapContributor**: Extension point for custom role/permission logic. Multiple contributors can be registered and are invoked during attribution.

**IRoleConfigSnapshotProvider**: Caches role aliases and policy bindings from database to avoid per-request queries. Snapshot is reloaded on admin changes.

### Claims Transformation Pipeline

1. **Extract**: Read roles/permissions from configured claim keys
2. **Normalize**: Apply aliases (DB snapshot preferred over options)
3. **Contribute**: Invoke registered `IRoleMapContributor` instances
4. **Bootstrap**: Apply one-time admin elevation if configured
5. **Fallback**: Add dev fallback role if no roles found (Development only)
6. **Emit**: Add `ClaimTypes.Role` and optional permission claims
7. **Cache**: Store result with stable stamp for subsequent requests

### Options Configuration

**Section Path**: `Sora:Web:Auth:Roles`

**Key Options**:
- `ClaimKeys.Roles`: Claim types to read roles from (default: `["roles", "role", "groups", "sora:role", "sora:roles"]`)
- `ClaimKeys.Permissions`: Claim types to read permissions from
- `Aliases.Map`: Role name mappings (e.g., `"administrator": "admin"`)
- `EmitPermissionClaims`: Whether to add permission claims (default: `true`)
- `MaxRoles`/`MaxPermissions`: Caps to prevent bloat (default: 256/1024)
- `DevFallback`: Auto-add role in Development (default: enabled, role: `"reader"`)
- `Bootstrap`: One-time admin elevation configuration
- `AllowSeedingInProduction`: Production import safety gate

## Data Contracts

### Entities (First-Class Statics)

```csharp
// Role entity
public class Role : Entity<Role>, ISoraAuthRole
{
    public string? Display { get; set; }
    public string? Description { get; set; }
    public byte[]? RowVersion { get; set; }
}

// Usage
var roles = await Role.All(ct);
var admin = await Role.Get("admin", ct);
await Role.UpsertMany(new[] { new Role { Id = "editor" } }, ct);
```

**RoleAlias**: Maps alternative names to canonical roles
- `Id`: Alias key (e.g., "admins")
- `TargetRole`: Canonical role (e.g., "admin")

**RolePolicyBinding**: Maps authorization policies to role requirements
- `Id`: Policy name (e.g., "auth.roles.admin")
- `Requirement`: Role expression (e.g., "role:admin")

### Store Abstractions

**IRoleStore**: CRUD operations for roles
**IRoleAliasStore**: CRUD operations for aliases  
**IRolePolicyBindingStore**: CRUD operations for policy bindings

Default implementations delegate to Entity statics. Custom stores can be registered to use different schemas while preserving API contracts.

## Bootstrap Modes

### FirstUser
First authenticated user to trigger attribution gets admin role. State persisted in `RoleBootstrapState` entity with key `"admin-bootstrap"`.

### ClaimMatch
Specific users elevated based on claim values:
```json
{
  "Bootstrap": {
    "Mode": "ClaimMatch",
    "ClaimType": "email",
    "ClaimValues": ["admin@company.com"],
    "AdminEmails": ["cto@company.com"]  // Convenience for email claims
  }
}
```

Bootstrap is one-time only. State tracked in database to prevent replay.

## Admin API

**Base Route**: `/api/auth/roles`
**Authorization**: Requires `auth.roles.admin` policy

### Endpoints

- `GET /` - List all roles
- `PUT /{id}` - Create/update role
- `DELETE /{id}` - Delete role
- `GET /aliases` - List role aliases
- `PUT /aliases/{alias}` - Create/update alias
- `DELETE /aliases/{alias}` - Delete alias
- `GET /policy-bindings` - List policy bindings
- `PUT /policy-bindings/{policy}` - Create/update binding
- `DELETE /policy-bindings/{policy}` - Delete binding
- `GET /export` - Export current configuration
- `POST /import` - Import from options template
- `POST /reload` - Reload snapshot and clear cache

### Import Behavior

**Dry Run**: `POST /import?dryRun=true` returns diff without applying
**Force Mode**: `?force=true` deletes items not in template
**Production Guard**: Blocked unless `AllowSeedingInProduction` or `SoraEnv.AllowMagicInProduction`

## Performance Characteristics

### Caching Strategy
- **Attribution Cache**: Per-user results cached with stable stamp
- **Config Snapshot**: Aliases and policy bindings cached in memory
- **Cache Invalidation**: Cleared on admin mutations (import/reload)

### Optimization Notes
- Stamp-based no-op in claims transformer for cached results
- Snapshot avoids DB queries during attribution
- Contributors execute in registration order; exceptions logged but don't halt pipeline
- Role/permission sets use case-insensitive deduplication

## Security Considerations

### Production Safety
- Import/seeding disabled by default in Production
- Bootstrap state prevents re-elevation attacks
- Role/permission caps prevent claim bloat
- Admin API requires explicit policy authorization

### Bootstrap Security
- FirstUser mode: Race condition possible in high-concurrency scenarios
- ClaimMatch mode: Relies on trusted identity provider claims
- Bootstrap state persisted to prevent replay

## Extensibility

### Custom Contributors
Implement `IRoleMapContributor` for external role sources:

```csharp
public class DatabaseRoleContributor : IRoleMapContributor
{
    public async Task ContributeAsync(ClaimsPrincipal user, ISet<string> roles, 
        ISet<string> permissions, RoleAttributionContext? context, CancellationToken ct)
    {
        // Custom logic here
    }
}
```

### Custom Stores
Replace default stores for different schemas:

```csharp
services.AddSingleton<IRoleStore, CustomRoleStore>();
```

### Policy Integration
Use with `Sora.Web.Extensions` for capability-based policies:

```csharp
services.AddSoraWebAuthRoles()
    .AddSoraRolePolicies();
```

## Error Handling

### Contributor Failures
Contributors that throw exceptions are logged and skipped. Attribution continues with remaining contributors.

### Bootstrap Failures
Bootstrap state persistence failures prevent elevation but don't crash the request. Logged as warnings.

### Cache Failures
Cache operations are best-effort. Attribution works without cache but may be slower.

## Dependencies

**Required**:
- `Sora.Core` (Entity statics, options binding)
- `Sora.Web.Extensions` (policy binding helpers)
- `Microsoft.AspNetCore.Authorization` (claims transformation)

**Optional**:
- Storage providers (auto-configured via Sora boot process)
- Custom contributors
- Policy binding extensions
