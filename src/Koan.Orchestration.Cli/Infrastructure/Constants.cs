namespace Koan.Orchestration.Cli.Infrastructure;

internal static class Constants
{
    public const string DefaultComposePath = ".Koan/compose.yml";
    public static readonly string[] DefaultProviderOrder = ["docker", "podman"]; // Podman pending
    public const string EnvPreferredProviders = "Koan_ORCHESTRATION_PREFERRED_PROVIDERS"; // comma-separated ids e.g., docker,podman
    public static readonly string[] OrchestrationDescriptorCandidates =
    [
        "Koan.orchestration.yml",
        "Koan.orchestration.yaml",
        "Koan.orchestration.json",
    ];

    // Additional compose probe locations for diagnostics/inspect (read-only hints)
    public static readonly string[] ComposeProbeCandidates =
    [
        ".Koan/compose.yml",
        "docker/compose.yml",
        "compose.yml"
    ];

    // Optional per-project overrides applied after discovery (JSON only for now)
    public static readonly string[] OverrideCandidates =
    [
        ".Koan/overrides.json",
        "overrides.Koan.json"
    ];
}
