using System.Runtime.CompilerServices;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

[Collection(ExecutableApplicationProbeCollection.Name)]
public sealed class FirstUseContractTests
{
    [Fact]
    public async Task SourceCheckoutProducesTheMeaningfulFirstResult()
    {
        var root = RepositoryRoot();
        var application = Path.Combine(root, "samples", "FirstUse");
        await new ProcessRunner().RequireAsync(
            "dotnet",
            ["build", "FirstUse.csproj", "-c", "Release", "--nologo", "-warnaserror"],
            application,
            TestContext.Current.CancellationToken);

        var evidence = await new FirstUseApplicationProbe().RunBuiltAsync(
            application,
            "source",
            TestContext.Current.CancellationToken);

        Assert.Equal("sqlite", evidence.SelectedAdapter);
        Assert.True(evidence.CompositionLockfileObserved);
        Assert.True(evidence.CompositionLockfileMatched);
        Assert.True(evidence.RestFilterObserved);
        Assert.True(evidence.StartupReported);
        Assert.True(evidence.FactsConverged);
        Assert.True(evidence.DryRunPreservedState);
        Assert.True(evidence.AgentMutationObserved);
        Assert.True(evidence.RemoteDeleteHidden);
        Assert.Equal(8, evidence.Steps.Count);
    }

    private static string RepositoryRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));
}
