using System.Runtime.CompilerServices;
using Koan.GoldenJourney.Domain;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

[Collection(ExecutableApplicationProbeCollection.Name)]
public sealed class GoldenJourneyContractTests
{
    [Fact]
    public void BusinessCodeExpressesTheRuleWithoutFrameworkCeremony()
    {
        var request = ReviewRequest.Open("Review a critical change", ReviewImpact.High, urgent: false);

        request.Assess();

        Assert.Equal(ReviewPriority.Critical, request.Priority);
    }

    [Fact]
    public async Task SourceCheckoutProducesTheCumulativeGoldenJourney()
    {
        var application = Path.Combine(RepositoryRoot(), "samples", "GoldenJourney");
        await new ProcessRunner().RequireAsync(
            "dotnet",
            ["build", PackagingConstants.GoldenJourney.ProjectFile, "-c", "Release", "--nologo", "-warnaserror"],
            application,
            TestContext.Current.CancellationToken);

        var evidence = await new GoldenJourneyApplicationProbe().RunBuiltAsync(
            application,
            "source",
            TestContext.Current.CancellationToken);

        Assert.True(evidence.BusinessRuleObserved);
        Assert.True(evidence.CompositionLockfileObserved);
        Assert.True(evidence.CompositionLockfileMatched);
        Assert.True(evidence.PersistenceObserved);
        Assert.True(evidence.ReactiveWorkObserved);
        Assert.True(evidence.JobsCompositionObserved);
        Assert.True(evidence.FactsConverged);
        Assert.True(evidence.CustomMutationSchemaTruthful);
        Assert.True(evidence.AgentBoundaryObserved);
        Assert.True(evidence.AgentMutationObserved);
        Assert.True(evidence.AdapterRejectionExplained);
        Assert.True(evidence.AdapterRejectionAffectedReadiness);
        Assert.True(evidence.RejectedWorkerLogsCalm);
        Assert.True(evidence.AdapterRecoveryObserved);
        Assert.Equal(11, evidence.Steps.Count);
    }

    private static string RepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
