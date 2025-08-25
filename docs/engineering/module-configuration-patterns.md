# Module configuration patterns (SoC + DX)

This page is a copy-paste blueprint for module authors to implement consistent configuration, validation, discovery, and health checks.

Contract
- Key scheme: `Sora:<Area>:<ModuleName>:<Alias>:<Property>`
- Default alias: `Default`; named aliases allowed
- Env vars: `SORA__...` mapping
- Options per module with `BindPath` constant + `ValidateOnStart()`
- One DI entrypoint per module: `AddSora<Module>(IConfiguration, alias = "Default")`
- Runtime access via `SoraEnv`; avoid raw `Environment.*`

Scaffold
1) Options (typed + defaults + validation)
2) Constants (no magic strings)
3) ServiceCollection extension (bind named options, validators, health)
4) Client factory with keyed registrations (by alias)
5) Health check (predictable name/tags)

Example (hypothetical: Sora.Data.Oracle)
// Options
```csharp
public sealed class OracleOptions
{
    public const string BindPath = "Sora:Data:Oracle";

    [Required]
    public string? ConnectionString { get; set; }

    [Range(1, 600)]
    public int CommandTimeoutSeconds { get; set; } = 30;

    public bool EnableAutoDiscovery { get; set; } = true; // Dev/CI true; Prod false
}
```

// Constants
```csharp
public static class OracleConstants
{
    public const string BindPath = OracleOptions.BindPath;
    public const string DefaultAlias = "Default";

    public static string SectionPath(string alias) => $"{BindPath}:{alias}";
    public static string HealthName(string alias) => $"Oracle:{alias}";
    public static string[] HealthTags(string alias) => new[] { "db", "oracle", alias };
}
```

// Extension
```csharp
public static class OracleServiceCollectionExtensions
{
    public static IServiceCollection AddSoraOracle(
        this IServiceCollection services,
        IConfiguration config,
        string alias = OracleConstants.DefaultAlias,
        Action<OracleOptions>? postConfigure = null)
    {
        services.AddOptions<OracleOptions>(alias)
            .Bind(config.GetSection(OracleConstants.SectionPath(alias)))
            .ValidateDataAnnotations()
            .PostConfigure(o => postConfigure?.Invoke(o))
            .ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<OracleOptions>, OracleOptionsValidator>());

        services.AddKeyedSingleton<IOracleClient>(alias, sp =>
        {
            var opts = sp.GetRequiredService<IOptionsMonitor<OracleOptions>>().Get(alias);
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("Oracle");
            var endpoint = OracleDiscovery.ResolveEndpoint(opts, logger); // implement discovery per module
            return new OracleClient(endpoint, opts);
        });

        services.AddHealthChecks().Add(new HealthCheckRegistration(
            OracleConstants.HealthName(alias),
            sp => new OracleHealthCheck(sp.GetRequiredKeyedService<IOracleClient>(alias)),
            default, OracleConstants.HealthTags(alias)));

        return services;
    }
}
```

// Validator
```csharp
internal sealed class OracleOptionsValidator(IHostEnvironment env) : IValidateOptions<OracleOptions>
{
    public ValidateOptionsResult Validate(string? name, OracleOptions options)
    {
        if (env.IsProduction() && string.IsNullOrWhiteSpace(options.ConnectionString))
            return ValidateOptionsResult.Fail($"[{name ?? OracleConstants.DefaultAlias}] ConnectionString is required in Production.");
        if (options.CommandTimeoutSeconds <= 0)
            return ValidateOptionsResult.Fail($"[{name ?? OracleConstants.DefaultAlias}] CommandTimeoutSeconds must be > 0.");
        return ValidateOptionsResult.Success;
    }
}
```

// Health check
```csharp
internal sealed class OracleHealthCheck(IOracleClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
        => (await client.PingAsync(ct)) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Oracle ping failed");
}
```

Discovery (Dev/CI, SoC)
- Implement a small `*Discovery` helper in the module that:
  1) Checks explicit config
  2) Applies env overrides
  3) Tries localhost default port
  4) Tries compose/Kubernetes hints when applicable
- Time budgets are lenient; log one success/warn line; redact secrets.

Startup (consumer)
```csharp
builder.AddSoraConfiguration();
builder.Services.AddSoraDefaults(builder.Configuration, builder.Environment);
builder.Services.AddSoraOracle(builder.Configuration);             // Default
builder.Services.AddSoraOracle(builder.Configuration, "Reporting"); // Named alias
```

See also
- ARCH-0044-standardized-module-config-and-discovery.md
- ARCH-0040-config-and-constants-naming.md
- ARCH-0039-soraenv-static-runtime.md
