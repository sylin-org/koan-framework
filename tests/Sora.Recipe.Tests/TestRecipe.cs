using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sora.Recipe;
using Sora.Core.Modules;

namespace Sora.Recipe.Tests;

public sealed class TestOptions
{
    public int? TimeoutMs { get; set; }
    public string? Endpoint { get; set; }
}

public sealed class TestRecipe : ISoraRecipe
{
    public string Name => "test";
    public int Order => 50; // foundation lane
    public bool ShouldApply(IConfiguration cfg, IHostEnvironment env) => true;

    public void Apply(IServiceCollection services, IConfiguration cfg, IHostEnvironment env)
    {
        // Layering: provider defaults < recipe defaults < config < code < forced overrides
        services.AddOptions<TestOptions>()
            .WithProviderDefaults(o => o.TimeoutMs ??= 1000)
            .WithRecipeDefaults(o => o.TimeoutMs ??= 1500)
            .BindFromConfiguration(cfg.GetSection("Sora:Tests:TestOptions"))
            .WithCodeOverrides(o => o.Endpoint ??= "http://code-override")
            .WithRecipeForcedOverridesIfEnabled(cfg, Name, o => o.TimeoutMs = Math.Min(o.TimeoutMs ?? 0, 500));
    }
}
