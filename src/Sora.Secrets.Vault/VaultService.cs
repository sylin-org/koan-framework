using Sora.Orchestration;
using Sora.Orchestration.Attributes;

namespace Sora.Secrets.Vault;

// Declarative service descriptor for HashiCorp Vault so Sora orchestration can spin it up for local/dev.
[SoraService(ServiceKind.SecretsVault, shortCode: "vault", name: "HashiCorp Vault",
    ContainerImage = "hashicorp/vault",
    DefaultTag = "1",
    DefaultPorts = new[] { 8200 },
    Capabilities = new[] { "protocol=http", "secrets=vault" },
    Volumes = new[] { "./Data/vault:/vault/data" },
    AppEnv = new[] { "Sora__Secrets__Vault__Address={scheme}://{host}:{port}" },
    HealthEndpoint = "/v1/sys/health",
    Scheme = "http", Host = "vault", EndpointPort = 8200, UriPattern = "http://{host}:{port}",
    LocalScheme = "http", LocalHost = "localhost", LocalPort = 8200, LocalPattern = "http://{host}:{port}")]
internal sealed class VaultService // no implementation needed; attribute carries the metadata
{
}
