using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Diagnostics;
using Koan.Core.Hosting.App;
using Koan.Media.Abstractions.Recipes;
using Koan.Media.Core.Recipes;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Media.Web.Tests;

[Collection("media-web")]
public sealed class MediaStartupContractSpec(MediaWebHostFixture fixture)
{
    [Fact]
    public void Startup_facts_report_materialized_recipe_decisions()
    {
        var facts = fixture.Host.Services.GetRequiredService<IKoanRuntimeFacts>().Current.Facts;

        facts.Should().ContainSingle(fact =>
            fact.Code == "koan.media.recipes.discovered"
            && fact.Subject == "media:recipes"
            && fact.Summary.Contains("2 named Media recipe(s)", StringComparison.Ordinal));
        facts.Should().ContainSingle(fact =>
            fact.Code == "koan.media.recipe.discovered"
            && fact.Subject == "media:recipe:startup-card"
            && fact.Summary.Contains("fingerprint", StringComparison.Ordinal)
            && fact.Summary.Contains("2 step(s)", StringComparison.Ordinal));
        facts.Should().ContainSingle(fact =>
            fact.Code == "koan.media.recipe.discovered"
            && fact.Subject == "media:recipe:configured-card"
            && fact.Summary.Contains("Config", StringComparison.Ordinal)
            && fact.Summary.Contains("2 step(s)", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_recipe_registry_fails_host_startup()
    {
        var current = AppHost.Current;
        try
        {
            var start = () => KoanIntegrationHost.Configure()
                .WithSetting("Koan:Environment", "Test")
                .WithSetting("Koan:Media:Recipes:invalid-startup:Steps:0:Op", "not-a-media-operation")
                .ConfigureServices(services =>
                {
                    services.AddKoan();
                })
                .StartAsync();

            var failure = (await start.Should().ThrowAsync<MediaRecipeBindingException>()).Which;
            failure.Message.Should().Contain("invalid-startup")
                .And.Contain("not-a-media-operation");
        }
        finally
        {
            AppHost.Current = current;
        }
    }
}

internal static class MediaStartupRecipes
{
    [MediaRecipe("startup-card", Description = "Recipe used to prove startup discovery and reporting.")]
    internal static MediaRecipe StartupCard() => MediaRecipe.New()
        .Resize(width: 320)
        .EncodeAs("jpeg", Quality.Web)
        .Build();
}
