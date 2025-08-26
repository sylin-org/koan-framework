using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Sora.Data.Core;
using Sora.Recipe;
using Xunit;

namespace Sora.Recipe.Tests;

public class RecipeTests
{
    [Fact]
    public void ObservabilityRecipe_applies_by_reference_default()
    {
        var services = new ServiceCollection();
        // Minimal config
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        services.AddSingleton<IConfiguration>(cfg);

        services.AddSora(); // triggers recipe pipeline

        var sp = services.BuildServiceProvider();
        var hc = sp.GetService<HealthCheckService>();
        hc.Should().NotBeNull("observability recipe should register health checks when available");
    }

    [Fact]
    public void DryRun_skips_application_and_logs()
    {
        var services = new ServiceCollection();
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sora:Recipes:DryRun"] = "true",
            ["Sora:Recipes:Active"] = "observability"
        }).Build();
        services.AddSingleton<IConfiguration>(cfg);

        services.AddSora();

        var sp = services.BuildServiceProvider();
        var hc = sp.GetService<HealthCheckService>();
        hc.Should().BeNull("dry-run should not mutate DI");
    }

    [Fact]
    public void Options_layering_respects_precedence()
    {
        var services = new ServiceCollection();
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sora:Recipes:Active"] = "test",
            ["Sora:Tests:TestOptions:TimeoutMs"] = "2000" // config overrides both defaults
        }).Build();
        services.AddSingleton<IConfiguration>(cfg);

        // Register the test recipe before AddSora so the bootstrapper applies it
        services.AddRecipe<TestRecipe>();
        services.AddSora();

        var sp = services.BuildServiceProvider();
        var val = sp.GetRequiredService<IOptions<TestOptions>>().Value;
        val.TimeoutMs.Should().Be(2000); // config wins over provider/recipe defaults
        val.Endpoint.Should().Be("http://code-override"); // code override fills when unset in config
    }

    [Fact]
    public void Forced_overrides_apply_only_when_gates_enabled()
    {
        var services = new ServiceCollection();
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sora:Recipes:Active"] = "test",
            ["Sora:Recipes:AllowOverrides"] = "true",
            ["Sora:Recipes:test:ForceOverrides"] = "true",
            ["Sora:Tests:TestOptions:TimeoutMs"] = "1200"
        }).Build();
        services.AddSingleton<IConfiguration>(cfg);

    services.AddRecipe<TestRecipe>();
    services.AddSora();
        var sp = services.BuildServiceProvider();
        var opts = sp.GetRequiredService<IOptions<TestOptions>>().Value;
        // Forced override caps to 500
        opts.TimeoutMs.Should().Be(500);
    }
}
