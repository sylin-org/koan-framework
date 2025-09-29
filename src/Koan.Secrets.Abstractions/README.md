# Koan.Secrets.Abstractions

## Contract
- **Purpose**: Define contracts and options for secret providers within the Koan framework.
- **Primary inputs**: `SecretDescriptor`, `SecretScope`, and provider capability definitions consumed by runtime modules.
- **Outputs**: Typed options, serialization helpers, and provider interfaces used by Koan secrets implementations.
- **Failure modes**: Providers failing to implement required interfaces, mismatched secret scopes, or serialization incompatibilities when transporting secrets.
- **Success criteria**: Secret providers register seamlessly, secrets resolve with scoped context, and options validation guards against misconfiguration.

## Quick start
```csharp
using Koan.Secrets.Abstractions;

public sealed class ApiKeySecret : SecretDescriptor
{
    public ApiKeySecret() : base("stripe:api-key")
    {
        Scopes.Add(SecretScope.Environment("Production"));
        Metadata["rotation"] = "30d";
    }
}

public sealed class SecretsAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Secrets";

    public void Initialize(IServiceCollection services)
        => services.AddSecretDescriptor<ApiKeySecret>();

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
        => report.AddNote("Stripe API key descriptor registered");
}
```
- Describe secrets using strongly-typed descriptors; providers leverage metadata when fetching material.
- Register descriptors through auto-registrar to enable discovery and rotation tooling.

## Configuration
- Use `SecretProviderOptions` to configure provider-specific endpoints or authentication.
- Bind scope defaults from configuration (e.g., `Koan:Secrets:DefaultScope`).
- Combine with Koan adapters to expose secret availability in boot reports.

## Edge cases
- Missing descriptors: runtime logs warnings when a secret is requested without a descriptor—add descriptors for every consumer-facing secret.
- Scope mismatches: ensure requesting code passes the same tenant/environment scope used by the descriptor.
- Rotation windows: track `Metadata` values to align with external rotation policies.
- Serialization: keep secret payloads simple (strings/JSON) to avoid deserialization issues in provider implementations.

## Related packages
- `Koan.Secrets.Core` – uses these abstractions to orchestrate providers.
- `Koan.Secrets.Vault` – concrete provider built on top of the abstractions.
- `Koan.Core` – options binding and boot reporting infrastructure.

## Reference
- `SecretDescriptor` – core descriptor base class.
- `ISecretProvider` – interface implemented by provider modules.
- `SecretScope` – helpers for environment/tenant scoping.
