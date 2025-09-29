# Koan.Recipe.Abstractions

## Contract
- **Purpose**: Define recipe contracts for bundling Koan modules into intention-driven runtime profiles (observability, auth, storage, etc.).
- **Primary inputs**: Classes marked with `[KoanRecipe]`, recipe capability descriptors, and DI registration helpers.
- **Outputs**: Registered recipes accessible via `IKoanRecipeRegistry`, capability metadata for discovery, and staged initialization gates.
- **Failure modes**: Recipe mis-declared (missing attribute), unmet capability gates, or initializer exceptions during bootstrap.
- **Success criteria**: Recipes register cleanly, advertise capabilities, and initialize their services in the expected order.

## Quick start
```csharp
[KoanRecipe("observability:otel", DisplayName = "Observability baseline")]
public sealed class ObservabilityRecipe : IKoanRecipe
{
    public void Describe(RecipeCapabilities caps)
        => caps.Provides("otel:tracing").Requires("storage:default");

    public void ConfigureServices(IKoanRecipeContext context)
    {
        context.Services.AddOpenTelemetry();
        context.Log("Registered observability pipeline");
    }
}

public sealed class RecipeAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Recipes";

    public void Initialize(IServiceCollection services)
        => services.AddKoanRecipe<ObservabilityRecipe>();

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
        => report.AddNote("Observability recipe available");
}
```
- Use the `[KoanRecipe]` attribute to name and classify recipes.
- Implement `IKoanRecipe` to declare capabilities and register services in a controlled order.

## Configuration
- Guard recipes with `RecipeGates` to require environment checks or secrets before running.
- Inject configuration via `KoanRecipeOptions` to allow per-environment tuning.
- Use `RecipeApplier` to activate recipes programmatically (e.g., CLI).

## Edge cases
- Conflicting recipes: ensure capability requirements don’t create circular dependencies; use `Requires()` and `Provides()` consistently.
- Partial activation: handle `RecipeInitializer` failures by logging and short-circuiting subsequent stages.
- Multi-tenant setups: register tenant-specific recipes under unique IDs to avoid collisions.
- Hot reload: recipes run during bootstrap; for dynamic changes use Koan orchestration instead.

## Related packages
- `Koan.Recipe.Observability` – concrete recipe implementation building on these abstractions.
- `Koan.Core` – DI primitives and boot logging used by recipes.
- `Koan.Orchestration.Abstractions` – can activate recipes as part of environment provisioning.

## Reference
- `IKoanRecipe` – core interface for recipe implementations.
- `RecipeCapabilities` – DSL for declaring requirements and features.
- `RecipeRegistry` – lookup surface for activated recipes.
