using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Analytics.Initialization;
using Koan.Testing.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Data.Analytics.Tests.Specs;

/// <summary>
/// The composition gate: a declared analytics grammar with no elected engine refuses to start, and the
/// refusal names the package that provides one — so the failure is a corrective, not a mystery.
/// </summary>
public sealed class AnalyticsElectionSpec
{
    [Fact]
    public async Task A_host_without_an_elected_engine_refuses_with_a_corrective()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton<IHostEnvironment>(new TestEnvironment());
        services.AddLogging();
        services.AddKoanOptions<AnalyticsOptions>();

        var module = new AnalyticsModule();
        module.Register(services);
        var provider = services.BuildServiceProvider();

        // The gate keys on usage: a DECLARED question with no engine refuses. (The no-questions path —
        // where a transitive module reference is harmless — is proven by every sibling suite that boots
        // AddKoan with this module present and zero questions declared.)
        Analytics.Question<AnalyticsProbe, string>("election-spec-with-engine", q => q.Count());
        var refusal = (await FluentActions.Awaiting(() => module.Start(provider, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>()).Which;

        refusal.Message.Should().Contain("Sylin.Koan.Data.Connector.DuckDb",
            "the refusal must name the package that provides the reference engine");
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Koan.Data.Analytics.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.PhysicalFileProvider(AppContext.BaseDirectory);
    }
}
