using Koan.Core.Hosting.App;
using Koan.Data.Analytics;
using Koan.Data.Analytics.Recipes;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Analytics.Tests;

/// <summary>The entity the analytics specs ask questions about. Stored on the ambient SQLite store.</summary>
public sealed class AnalyticsProbe : Entity<AnalyticsProbe>
{
    public string Name { get; set; } = "";
    public int Priority { get; set; }
    public decimal Score { get; set; }
}

/// <summary>Shared helpers: declaration with unique names and known seed data.</summary>
internal static class AnalyticsProbeSetup
{
    public static async Task Seed(IServiceProvider services)
    {
        AppHost.Current = services;
        await new AnalyticsProbe { Name = "alpha", Priority = 1, Score = 10m }.Save();
        await new AnalyticsProbe { Name = "beta", Priority = 2, Score = 20m }.Save();
        await new AnalyticsProbe { Name = "alpha", Priority = 2, Score = 30m }.Save();
        await new AnalyticsProbe { Name = "gamma", Priority = 3, Score = 40m }.Save();
    }

    public static AnalyticsQuestion<AnalyticsProbe, string> Declare(string name, Action<AnalyticsRecipe<AnalyticsProbe, string>>? configure = null, int? rowCap = null) =>
        Analytics.Question<AnalyticsProbe, string>(name, r => configure?.Invoke(r), rowCap);
}
