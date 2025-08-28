namespace Sora.Orchestration.Cli.Infrastructure;

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

    // Additional compose probe locations for diagnostics/inspect (read-only hints)
    public static readonly string[] ComposeProbeCandidates =
    [
        ".sora/compose.yml",
        "docker/compose.yml",
        "compose.yml"
    ];

    // Optional per-project overrides applied after discovery (JSON only for now)
    public static readonly string[] OverrideCandidates =
    [
        ".sora/overrides.json",
        "overrides.sora.json"
    ];
}
