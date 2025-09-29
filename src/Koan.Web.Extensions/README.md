# Koan.Web.Extensions

## Contract

- **Purpose**: Deliver optional HTTP capabilities for Koan Web applications (moderation, audit, soft delete, capability controllers).
- **Primary inputs**: Entity models exposed through Koan controllers, capability descriptors, and policy configuration.
- **Outputs**: Extension controllers, authorization policies, moderation workflows, and capability metadata for clients.
- **Failure modes**: Missing capability registrations, conflicts with custom routing, or policies referencing undefined roles.
- **Success criteria**: Extensions register only when needed, controllers expose consistent routes, and policy enforcement remains predictable.

## Quick start

```csharp
using Koan.Web.Extensions;

public sealed class WebExtensionsAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "WebExtensions";

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanWebExtensions(options =>
        {
            options.EnableModeration = true;
            options.EnableSoftDelete = true;
        });
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
        => report.AddNote("Web extensions enabled (moderation + soft delete)");
}
```

- Register the extensions in your auto-registrar; they hook into Koan Web controllers without manual endpoint wiring.
- Use capability attributes to expose moderation and audit features in generated clients.

## Configuration

- `Koan:Web:Extensions:EnableModeration` – toggles moderation controllers.
- `Koan:Web:Extensions:AuditLogRetention` – controls audit retention windows.
- `Koan:Web:Extensions:PolicyMappings` – map capabilities to authorization policies.

## Edge cases

- Multi-tenant moderation: ensure capability routes include tenant identifiers to prevent cross-tenant actions.
- Soft delete conflicts: coordinate with domain logic to avoid double-deleting resources.
- Authorization policies: missing roles cause 403 responses; confirm policies exist when enabling capabilities.
- Client regeneration: when enabling new capabilities, regenerate API clients to pick up metadata.

## Related packages

- `Koan.Web` – base web framework hosting these extensions.
- `Koan.Canon.Web` – can consume capability metadata for orchestration.
- `Koan.Data.Core` – used for moderation/audit persistence.

## Reference

- `ModerationController` – handles moderation workflows.
- `CapabilityController` – surfaces capability metadata.
- `WebExtensionsOptions` – options class for enabling features.
