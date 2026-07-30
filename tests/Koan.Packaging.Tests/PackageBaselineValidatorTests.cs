using System.Runtime.CompilerServices;
using Koan.Packaging.Models;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class PackageBaselineValidatorTests
{
    [Fact]
    public async Task MissingPublicPackageIsAnEmptyFirstPublicationHistory()
    {
        using var client = new HttpClient(new NotFoundHandler())
        {
            BaseAddress = new Uri("https://api.nuget.org/v3-flatcontainer/")
        };

        var versions = await PackageBaselineValidator.ReadNuGetVersionsAsync(
            client,
            "Sylin.Koan.New",
            CancellationToken.None);

        Assert.Empty(versions);
    }

    [Fact]
    public void MainPublisherRunsTheBaselineGuardBeforePack()
    {
        var workflow = File.ReadAllText(Path.Combine(FindKoanRoot(), ".github", "workflows", "release-on-main.yml"));
        var guard = workflow.IndexOf("dotnet run --project tools/Koan.Packaging -- api-baselines", StringComparison.Ordinal);
        var pack = workflow.IndexOf("- name: Pack", StringComparison.Ordinal);

        Assert.True(guard >= 0, "release-on-main must execute the real api-baselines command");
        Assert.True(pack > guard, "the API-baseline guard must pass before any package is packed");
        Assert.Contains("supported-foundation", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("versionIntent -eq '0.20'", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("dotnet test", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("green-ratchet", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AcceptsTheEarliestPublicDeclaredLineBaselineAndContentOnlyOwner()
    {
        var assembly = Project(
            "Sylin.Koan.Assembly",
            versionIntent: "0.21",
            baseline: "0.21.2",
            validationEnabled: true);
        var content = Project("Sylin.Koan.Content", includeBuildOutput: false);
        var validator = Validator(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [assembly.PackageId] = ["0.20.4", "0.21.2", "0.21.3", "0.21.4-beta"]
        });

        var report = await validator.ValidateAsync(
            [assembly, content],
            Surface(assembly.PackageId, content.PackageId),
            CancellationToken.None);

        Assert.Equal(2, report.SupportedOwners);
        Assert.Equal(1, report.AssemblyOwners);
        Assert.Equal(1, report.ConfiguredBaselines);
        Assert.Equal(0, report.FirstPublicationPending);
        Assert.Equal(1, report.ContentOnlyOwners);
    }

    [Fact]
    public async Task AllowsOnlyTheFirstPublicationToHaveNoBaseline()
    {
        var package = Project("Sylin.Koan.New", versionIntent: "0.21");
        var validator = Validator(new Dictionary<string, IReadOnlyList<string>>
        {
            [package.PackageId] = ["0.20.6"]
        });

        var report = await validator.ValidateAsync([package], Surface(package.PackageId), CancellationToken.None);

        Assert.Equal(1, report.FirstPublicationPending);
        Assert.Equal(0, report.ConfiguredBaselines);
    }

    [Fact]
    public async Task RejectsASecondPublicationOnTheDeclaredLineWithoutABaseline()
    {
        var package = Project("Sylin.Koan.Missing", versionIntent: "0.21");
        var validator = Validator(new Dictionary<string, IReadOnlyList<string>>
        {
            [package.PackageId] = ["0.20.6", "0.21.0"]
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.ValidateAsync([package], Surface(package.PackageId), CancellationToken.None));

        Assert.Contains(package.PackageId, error.Message, StringComparison.Ordinal);
        Assert.Contains("no PackageValidationBaselineVersion", error.Message, StringComparison.Ordinal);
        Assert.Contains("earliest immutable", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsALaterPatchAsTheBaseline()
    {
        var package = Project(
            "Sylin.Koan.Late",
            versionIntent: "0.21",
            baseline: "0.21.3",
            validationEnabled: true);
        var validator = Validator(new Dictionary<string, IReadOnlyList<string>>
        {
            [package.PackageId] = ["0.20.1", "0.21.1", "0.21.3"]
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.ValidateAsync([package], Surface(package.PackageId), CancellationToken.None));

        Assert.Contains("earliest immutable", error.Message, StringComparison.Ordinal);
        Assert.Contains("0.21.1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsABaselineFromAnOlderCompatibilityLine()
    {
        var package = Project(
            "Sylin.Koan.Breaking",
            versionIntent: "0.21",
            baseline: "0.20.1",
            validationEnabled: true);
        var validator = Validator(new Dictionary<string, IReadOnlyList<string>>
        {
            [package.PackageId] = ["0.20.1", "0.21.0"]
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.ValidateAsync([package], Surface(package.PackageId), CancellationToken.None));

        Assert.Contains("0.21.patch", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsAConfiguredBaselineWhenSdkValidationIsInactive()
    {
        var package = Project("Sylin.Koan.Inactive", baseline: "0.20.1");
        var validator = Validator(new Dictionary<string, IReadOnlyList<string>>
        {
            [package.PackageId] = ["0.20.1"]
        });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.ValidateAsync([package], Surface(package.PackageId), CancellationToken.None));

        Assert.Contains("EnablePackageValidation", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsAnApiBaselineOnAContentOnlyOwner()
    {
        var package = Project(
            "Sylin.Koan.Content",
            includeBuildOutput: false,
            baseline: "0.20.1",
            validationEnabled: true);
        var validator = Validator(new Dictionary<string, IReadOnlyList<string>>());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.ValidateAsync([package], Surface(package.PackageId), CancellationToken.None));

        Assert.Contains("Content-only", error.Message, StringComparison.Ordinal);
        Assert.Contains("artifact/dependency-shape", error.Message, StringComparison.Ordinal);
    }

    private static PackageBaselineValidator Validator(
        IReadOnlyDictionary<string, IReadOnlyList<string>> versions) =>
        new((packageId, _) => Task.FromResult(
            versions.TryGetValue(packageId, out var found) ? found : (IReadOnlyList<string>)[]));

    private static ProductSurface Surface(params string[] packageIds) => new()
    {
        Source = "product/claims.json",
        Claims =
        [
            new ProductClaim(
                "supported",
                "Supported",
                "Supported outcome.",
                "supported-extension",
                packageIds,
                ["docs/contract.md"],
                ["tests/evidence"])
        ]
    };

    private static PackageProject Project(
        string packageId,
        bool includeBuildOutput = true,
        string versionIntent = "0.20",
        string? baseline = null,
        bool validationEnabled = false) =>
        new(
            $"src/{packageId}/{packageId}.csproj",
            $"src/{packageId}",
            packageId,
            "Dependency",
            ["net10.0"],
            false,
            false,
            includeBuildOutput,
            false,
            true,
            "README.md",
            true,
            "TECHNICAL.md",
            "Description",
            "koan;test",
            [],
            VersionIntent: versionIntent,
            PackageValidationBaselineVersion: baseline,
            EnablePackageValidation: validationEnabled);

    private static string FindKoanRoot([CallerFilePath] string sourceFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", ".."));

    private sealed class NotFoundHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }
}
