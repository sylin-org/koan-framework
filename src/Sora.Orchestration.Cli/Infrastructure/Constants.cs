namespace Sora.Orchestration.Cli;

internal static class Constants
{
    public const string DefaultComposePath = ".sora/compose.yml";
    public static readonly string[] DefaultProviderOrder = ["docker", "podman"]; // Podman pending
    public const string EnvPreferredProviders = "SORA_ORCHESTRATION_PREFERRED_PROVIDERS"; // comma-separated ids e.g., docker,podman
    public static readonly string[] OrchestrationDescriptorCandidates =
    [
        "sora.orchestration.yml",
        "sora.orchestration.yaml",
        "sora.orchestration.json",
    ];
}
