using System.Security.Cryptography;
using System.Text.Json;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class ReleaseWaveCoordinatorTests
{
    [Fact]
    public void InspectionStateSerializesAsLowercaseVocabulary()
    {
        var inspection = new ReleaseWaveInspection(
            ReleaseWaveEscrowState.Prepared,
            $"{PackagingConstants.ReleaseWave.TagPrefix}{WaveFixture.VersionCommit}",
            WaveFixture.VersionCommit,
            "release-1");

        var json = JsonSerializer.Serialize(
            inspection,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"state\":\"prepared\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyWaveNeverCreatesEscrowOrTag()
    {
        using var fixture = await WaveFixture.CreateEmptyAsync();
        var escrow = new FakeEscrow();
        var target = new FakePromotionTarget();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Coordinator(escrow, target).StageAsync(
                fixture.MarkerPath,
                CancellationToken.None));

        Assert.Contains("empty release wave", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, escrow.CreateCount);
        Assert.Empty(target.Operations);
    }

    [Fact]
    public async Task StageResetsOnlyMarkerlessStarterAndUploadsPreparedMarkerLast()
    {
        using var fixture = await WaveFixture.CreateAsync();
        var escrow = new FakeEscrow();
        var target = new FakePromotionTarget();
        escrow.SeedStarter(fixture.Marker.TagName, fixture.Marker.VersionCommit);
        var coordinator = fixture.Coordinator(escrow, target);

        var initial = await coordinator.InspectAsync(fixture.Marker.VersionCommit, CancellationToken.None);
        var staged = await coordinator.StageAsync(fixture.MarkerPath, CancellationToken.None);
        var inspected = await coordinator.InspectAsync(fixture.Marker.VersionCommit, CancellationToken.None);

        Assert.Equal(ReleaseWaveEscrowState.Staging, initial.State);
        Assert.Equal(ReleaseWaveEscrowState.Prepared, staged.State);
        Assert.Equal(ReleaseWaveEscrowState.Prepared, inspected.State);
        Assert.Equal(1, escrow.DeleteCount);
        Assert.Equal(
            [fixture.Marker.Bundle.FileName, PackagingConstants.ReleaseWave.MarkerFileName],
            escrow.UploadAttempts.TakeLast(2).Select(attempt => attempt.AssetName).ToArray());
    }

    [Theory]
    [InlineData("create")]
    [InlineData("delete")]
    [InlineData("bundle")]
    [InlineData("marker")]
    public async Task StageResponseLossConvergesWithoutReplacingUploadedMarkerAuthority(string mode)
    {
        using var fixture = await WaveFixture.CreateAsync();
        var escrow = new FakeEscrow();
        var target = new FakePromotionTarget();
        switch (mode)
        {
            case "create":
                escrow.FailCreateAfterMutationOnce = true;
                break;
            case "delete":
                escrow.SeedStarter(fixture.Marker.TagName, fixture.Marker.VersionCommit);
                escrow.FailDeleteAfterMutationOnce = true;
                break;
            case "bundle":
                escrow.FailBundleUploadAfterStoreOnce = true;
                break;
            case "marker":
                escrow.FailMarkerUploadAfterStoreOnce = true;
                break;
        }
        var coordinator = fixture.Coordinator(escrow, target);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StageAsync(fixture.MarkerPath, CancellationToken.None));
        var recovered = await coordinator.StageAsync(
            fixture.MarkerPath,
            CancellationToken.None);

        Assert.Equal(ReleaseWaveEscrowState.Prepared, recovered.State);
        Assert.Equal(mode == "marker" ? 0 : 1, escrow.DeleteCount);
        Assert.True(escrow.HasUploadedAsset(
            fixture.Marker.TagName,
            PackagingConstants.ReleaseWave.MarkerFileName));
    }

    [Fact]
    public async Task PromotionRecoversPackageThenSymbolFailureFromExactEscrowBytes()
    {
        using var fixture = await WaveFixture.CreateAsync();
        var escrow = new FakeEscrow();
        var target = new FakePromotionTarget { FailSymbolsOnce = true };
        var coordinator = fixture.Coordinator(escrow, target);
        await coordinator.StageAsync(fixture.MarkerPath, CancellationToken.None);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.PromoteAsync(fixture.Marker.VersionCommit, CancellationToken.None));
        Assert.Contains("injected symbol failure", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(target.IsPublic(WaveFixture.PackageId, WaveFixture.Version));
        Assert.True(escrow.SingleRelease(fixture.Marker.TagName).IsDraft);
        Assert.False(escrow.HasAsset(fixture.Marker.TagName, ReleaseWaveCompletion.FileName));

        // Recovery has no dependency on the runner's original prepared files.
        Directory.Delete(fixture.OutputDirectory, recursive: true);
        var recovered = await coordinator.PromoteAsync(
            fixture.Marker.VersionCommit,
            CancellationToken.None);

        Assert.Equal(ReleaseWaveEscrowState.Published, recovered.State);
        Assert.Single(target.PackageAttempts);
        Assert.Equal(2, target.SymbolAttempts.Count);
        Assert.All(
            target.SymbolAttempts,
            attempt => Assert.Equal(fixture.SymbolBytes, attempt.Bytes));
        Assert.True(escrow.HasAsset(fixture.Marker.TagName, ReleaseWaveCompletion.FileName));
        Assert.True(escrow.SingleRelease(fixture.Marker.TagName).IsImmutable);
    }

    [Theory]
    [InlineData("package-response")]
    [InlineData("registry-wait")]
    public async Task PromotionRecoversPackageOrVisibilityResponseLossWithoutRepushingNupkg(string mode)
    {
        using var fixture = await WaveFixture.CreateAsync();
        var escrow = new FakeEscrow();
        var target = new FakePromotionTarget
        {
            FailPackageAfterStoreOnce = mode == "package-response",
            FailWaitOnce = mode == "registry-wait"
        };
        var coordinator = fixture.Coordinator(escrow, target);
        await coordinator.StageAsync(fixture.MarkerPath, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.PromoteAsync(fixture.Marker.VersionCommit, CancellationToken.None));
        Assert.True(target.IsPublic(WaveFixture.PackageId, WaveFixture.Version));
        Assert.False(escrow.HasAsset(fixture.Marker.TagName, ReleaseWaveCompletion.FileName));

        var recovered = await coordinator.PromoteAsync(
            fixture.Marker.VersionCommit,
            CancellationToken.None);

        Assert.Equal(ReleaseWaveEscrowState.Published, recovered.State);
        Assert.Single(target.PackageAttempts);
        Assert.Equal(mode == "package-response" ? 1 : 2, target.SymbolAttempts.Count);
    }

    [Fact]
    public async Task StageBlocksPublicIdentityWithoutPreparedEscrowBeforeMutation()
    {
        using var fixture = await WaveFixture.CreateAsync();
        var escrow = new FakeEscrow();
        var target = new FakePromotionTarget();
        target.SeedPublic(WaveFixture.PackageId, WaveFixture.Version, fixture.PackageBytes);
        var coordinator = fixture.Coordinator(escrow, target);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StageAsync(fixture.MarkerPath, CancellationToken.None));

        Assert.Contains("public package identity", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, escrow.CreateCount);
        Assert.Empty(escrow.UploadAttempts);
    }

    [Fact]
    public async Task StageNeverReplacesDifferentPreparedBitsUnderTheSameIdentity()
    {
        using var first = await WaveFixture.CreateAsync("first nupkg bytes", "first symbols bytes");
        using var second = await WaveFixture.CreateAsync("other nupkg bytes", "other symbols bytes");
        var escrow = new FakeEscrow();
        var target = new FakePromotionTarget();
        await first.Coordinator(escrow, target).StageAsync(first.MarkerPath, CancellationToken.None);
        var originalBundle = escrow.AssetBytes(first.Marker.TagName, first.Marker.Bundle.FileName);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            second.Coordinator(escrow, target).StageAsync(second.MarkerPath, CancellationToken.None));

        Assert.Contains("different prepared bytes", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, escrow.DeleteCount);
        Assert.Equal(originalBundle, escrow.AssetBytes(first.Marker.TagName, first.Marker.Bundle.FileName));
    }

    [Theory]
    [InlineData("missing-bundle")]
    [InlineData("tampered-bundle")]
    [InlineData("tampered-marker")]
    public async Task UploadedPreparedAuthorityFailsClosedWhenAssetsAreMissingOrTampered(string mode)
    {
        using var fixture = await WaveFixture.CreateAsync();
        var escrow = new FakeEscrow();
        var target = new FakePromotionTarget();
        var coordinator = fixture.Coordinator(escrow, target);
        await coordinator.StageAsync(fixture.MarkerPath, CancellationToken.None);
        target.Operations.Clear();
        switch (mode)
        {
            case "missing-bundle":
                escrow.RemoveAsset(fixture.Marker.TagName, fixture.Marker.Bundle.FileName);
                break;
            case "tampered-bundle":
                escrow.TamperAsset(fixture.Marker.TagName, fixture.Marker.Bundle.FileName);
                break;
            case "tampered-marker":
                escrow.TamperAsset(fixture.Marker.TagName, PackagingConstants.ReleaseWave.MarkerFileName);
                break;
        }

        var promoteError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.PromoteAsync(fixture.Marker.VersionCommit, CancellationToken.None));
        var stageError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.StageAsync(fixture.MarkerPath, CancellationToken.None));

        Assert.Contains("not valid exact escrow", promoteError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not valid exact escrow", stageError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, escrow.DeleteCount);
        Assert.Empty(target.Operations);
    }

    [Theory]
    [InlineData("release-target")]
    [InlineData("tag-target")]
    public async Task WrongReleaseOrTagTargetFailsBeforePromotion(string mode)
    {
        using var fixture = await WaveFixture.CreateAsync();
        var escrow = new FakeEscrow();
        var target = new FakePromotionTarget();
        var coordinator = fixture.Coordinator(escrow, target);
        await coordinator.StageAsync(fixture.MarkerPath, CancellationToken.None);
        target.Operations.Clear();
        if (mode == "release-target")
        {
            escrow.SetReleaseTarget(fixture.Marker.TagName, WaveFixture.OtherCommit);
        }
        else
        {
            escrow.SetTagTarget(fixture.Marker.TagName, WaveFixture.OtherCommit);
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.PromoteAsync(fixture.Marker.VersionCommit, CancellationToken.None));

        Assert.Contains("expected", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(target.Operations);
    }

    [Fact]
    public async Task CompletionUploadResponseLossReusesExactReceiptAndConverges()
    {
        using var fixture = await WaveFixture.CreateAsync();
        var escrow = new FakeEscrow { FailCompletionUploadAfterStoreOnce = true };
        var target = new FakePromotionTarget();
        var coordinator = fixture.Coordinator(escrow, target);
        await coordinator.StageAsync(fixture.MarkerPath, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.PromoteAsync(fixture.Marker.VersionCommit, CancellationToken.None));
        Assert.True(escrow.HasAsset(fixture.Marker.TagName, ReleaseWaveCompletion.FileName));
        Assert.True(escrow.SingleRelease(fixture.Marker.TagName).IsDraft);

        var recovered = await coordinator.PromoteAsync(
            fixture.Marker.VersionCommit,
            CancellationToken.None);

        Assert.Equal(ReleaseWaveEscrowState.Published, recovered.State);
        Assert.Equal(
            1,
            escrow.UploadAttempts.Count(attempt =>
                attempt.AssetName == ReleaseWaveCompletion.FileName));
        Assert.Equal(2, target.SymbolAttempts.Count);
    }

    [Fact]
    public async Task CompletionStarterIsReplacedWithoutTouchingPreparedAuthority()
    {
        using var fixture = await WaveFixture.CreateAsync();
        var escrow = new FakeEscrow();
        var target = new FakePromotionTarget();
        var coordinator = fixture.Coordinator(escrow, target);
        await coordinator.StageAsync(fixture.MarkerPath, CancellationToken.None);
        escrow.SeedStarterAsset(fixture.Marker.TagName, ReleaseWaveCompletion.FileName);

        var result = await coordinator.PromoteAsync(
            fixture.Marker.VersionCommit,
            CancellationToken.None);

        Assert.Equal(ReleaseWaveEscrowState.Published, result.State);
        Assert.True(escrow.HasUploadedAsset(
            fixture.Marker.TagName,
            ReleaseWaveCompletion.FileName));
        Assert.Equal(0, escrow.DeleteCount);
    }

    [Fact]
    public async Task PublishResponseLossIsRecoveredByTerminalInspection()
    {
        using var fixture = await WaveFixture.CreateAsync();
        var escrow = new FakeEscrow { FailPublishAfterMutationOnce = true };
        var target = new FakePromotionTarget();
        var coordinator = fixture.Coordinator(escrow, target);
        await coordinator.StageAsync(fixture.MarkerPath, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.PromoteAsync(fixture.Marker.VersionCommit, CancellationToken.None));
        var recovered = await coordinator.PromoteAsync(
            fixture.Marker.VersionCommit,
            CancellationToken.None);

        Assert.Equal(ReleaseWaveEscrowState.Published, recovered.State);
        Assert.Equal(1, escrow.PublishCount);
        Assert.Single(target.SymbolAttempts);
    }

    [Fact]
    public async Task PublishedReleaseWithTamperedCompletionFailsClosed()
    {
        using var fixture = await WaveFixture.CreateAsync();
        var escrow = new FakeEscrow();
        var target = new FakePromotionTarget();
        var coordinator = fixture.Coordinator(escrow, target);
        await coordinator.StageAsync(fixture.MarkerPath, CancellationToken.None);
        await coordinator.PromoteAsync(fixture.Marker.VersionCommit, CancellationToken.None);
        escrow.TamperAsset(fixture.Marker.TagName, ReleaseWaveCompletion.FileName);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.InspectAsync(fixture.Marker.VersionCommit, CancellationToken.None));

        Assert.Contains("not valid exact escrow", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublishedTerminalDoesNotDependOnLaterRegistryAvailability()
    {
        using var fixture = await WaveFixture.CreateAsync();
        var escrow = new FakeEscrow();
        var target = new FakePromotionTarget();
        var coordinator = fixture.Coordinator(escrow, target);
        await coordinator.StageAsync(fixture.MarkerPath, CancellationToken.None);
        await coordinator.PromoteAsync(fixture.Marker.VersionCommit, CancellationToken.None);
        target.RemovePublic(WaveFixture.PackageId, WaveFixture.Version);

        var inspection = await coordinator.InspectAsync(
            fixture.Marker.VersionCommit,
            CancellationToken.None);

        Assert.Equal(ReleaseWaveEscrowState.Published, inspection.State);
    }

    [Fact]
    public async Task PromotionUsesManifestDependencyOrderThenWaitsForTheWholeWave()
    {
        using var fixture = await WaveFixture.CreateTwoPackageAsync();
        var escrow = new FakeEscrow();
        var target = new FakePromotionTarget();
        var coordinator = fixture.Coordinator(escrow, target);
        await coordinator.StageAsync(fixture.MarkerPath, CancellationToken.None);
        target.Operations.Clear();

        await coordinator.PromoteAsync(fixture.Marker.VersionCommit, CancellationToken.None);

        Assert.Equal(
            [
                $"push:{WaveFixture.DependencyPackageId}/{WaveFixture.DependencyVersion}",
                $"symbols:{WaveFixture.DependencyPackageId}/{WaveFixture.DependencyVersion}",
                $"push:{WaveFixture.PackageId}/{WaveFixture.Version}",
                $"symbols:{WaveFixture.PackageId}/{WaveFixture.Version}",
                $"wait:{WaveFixture.DependencyPackageId}/{WaveFixture.DependencyVersion}",
                $"wait:{WaveFixture.PackageId}/{WaveFixture.Version}"
            ],
            target.Operations
                .Where(operation => !operation.StartsWith("exists:", StringComparison.Ordinal))
                .ToArray());
    }

    [Fact]
    public async Task DuplicateDraftOrPublishedDiscoveryFailsClosed()
    {
        using var fixture = await WaveFixture.CreateAsync();
        var escrow = new FakeEscrow();
        var target = new FakePromotionTarget();
        escrow.SeedStarter(fixture.Marker.TagName, fixture.Marker.VersionCommit);
        escrow.SeedStarter(fixture.Marker.TagName, fixture.Marker.VersionCommit);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Coordinator(escrow, target).InspectAsync(
                fixture.Marker.VersionCommit,
                CancellationToken.None));

        Assert.Contains("expected at most one", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class WaveFixture : IDisposable
    {
        public const string PackageId = "Sylin.Koan.Test";
        public const string Version = "1.2.3";
        public const string DependencyPackageId = "Sylin.Koan.Dependency";
        public const string DependencyVersion = "4.5.6";
        public const string PreviousVersionCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        public const string SourceCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        public const string VersionCommit = "cccccccccccccccccccccccccccccccccccccccc";
        public const string OtherCommit = "dddddddddddddddddddddddddddddddddddddddd";

        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        };

        private WaveFixture(string root, byte[] packageBytes, byte[] symbolBytes)
        {
            Root = root;
            PackageBytes = packageBytes;
            SymbolBytes = symbolBytes;
            ArtifactDirectory = Path.Combine(root, "artifacts");
            EvidenceDirectory = Path.Combine(root, "evidence");
            OutputDirectory = Path.Combine(root, "prepared");
            ScratchDirectory = Path.Combine(root, "scratch");
            Directory.CreateDirectory(ArtifactDirectory);
            Directory.CreateDirectory(EvidenceDirectory);
            Directory.CreateDirectory(OutputDirectory);
            LineagePath = Path.Combine(root, PackagingConstants.LineageArtifactFileName);
            ManifestPath = Path.Combine(root, PackagingConstants.ManifestFileName);
            PackageFileName = $"{PackageId}.{Version}.nupkg";
            SymbolsFileName = $"{PackageId}.{Version}.snupkg";
        }

        public string Root { get; }
        public string ArtifactDirectory { get; }
        public string EvidenceDirectory { get; }
        public string OutputDirectory { get; }
        public string ScratchDirectory { get; }
        public string LineagePath { get; }
        public string ManifestPath { get; }
        public string PackageFileName { get; }
        public string SymbolsFileName { get; }
        public string MarkerPath => Path.Combine(OutputDirectory, PackagingConstants.ReleaseWave.MarkerFileName);
        public byte[] PackageBytes { get; }
        public byte[] SymbolBytes { get; }
        public ReleaseWave Marker { get; private set; } = null!;

        public static Task<WaveFixture> CreateAsync() =>
            CreateAsync("exact nupkg bytes", "exact snupkg bytes");

        public static Task<WaveFixture> CreateAsync(string packageText, string symbolsText) =>
            CreateCoreAsync(packageText, symbolsText, includePackage: true, includeDependency: false);

        public static Task<WaveFixture> CreateEmptyAsync() =>
            CreateCoreAsync("unused nupkg", "unused symbols", includePackage: false, includeDependency: false);

        public static Task<WaveFixture> CreateTwoPackageAsync() =>
            CreateCoreAsync("dependent nupkg", "dependent symbols", includePackage: true, includeDependency: true);

        private static async Task<WaveFixture> CreateCoreAsync(
            string packageText,
            string symbolsText,
            bool includePackage,
            bool includeDependency)
        {
            var fixture = new WaveFixture(
                Path.Combine(Path.GetTempPath(), $"koan-wave-coordinator-{Guid.NewGuid():N}"),
                System.Text.Encoding.UTF8.GetBytes(packageText),
                System.Text.Encoding.UTF8.GetBytes(symbolsText));
            Directory.CreateDirectory(fixture.Root);
            if (includePackage)
            {
                await File.WriteAllBytesAsync(
                    Path.Combine(fixture.ArtifactDirectory, fixture.PackageFileName),
                    fixture.PackageBytes);
                await File.WriteAllBytesAsync(
                    Path.Combine(fixture.ArtifactDirectory, fixture.SymbolsFileName),
                    fixture.SymbolBytes);
            }

            var dependencyPackageFile = $"{DependencyPackageId}.{DependencyVersion}.nupkg";
            var dependencySymbolsFile = $"{DependencyPackageId}.{DependencyVersion}.snupkg";
            var dependencyPackageBytes = "dependency nupkg"u8.ToArray();
            var dependencySymbolsBytes = "dependency symbols"u8.ToArray();
            if (includeDependency)
            {
                await File.WriteAllBytesAsync(
                    Path.Combine(fixture.ArtifactDirectory, dependencyPackageFile),
                    dependencyPackageBytes);
                await File.WriteAllBytesAsync(
                    Path.Combine(fixture.ArtifactDirectory, dependencySymbolsFile),
                    dependencySymbolsBytes);
            }

            var lineagePackages = new List<ReleaseLineagePackage>();
            var manifestPackages = new List<ReleasePackage>();
            if (includeDependency)
            {
                lineagePackages.Add(new ReleaseLineagePackage(
                    DependencyPackageId,
                    "src/Koan.Dependency/Koan.Dependency.csproj",
                    DependencyVersion)
                {
                    SharedInputs = []
                });
                manifestPackages.Add(new ReleasePackage
                {
                    PackageId = DependencyPackageId,
                    Version = DependencyVersion,
                    ProjectPath = "src/Koan.Dependency/Koan.Dependency.csproj",
                    Reason = PackagingConstants.VersionChangedReason,
                    IncludeSymbols = true,
                    PackageFile = dependencyPackageFile,
                    PackageSha256 = Hash(dependencyPackageBytes),
                    SymbolsFile = dependencySymbolsFile,
                    SymbolsSha256 = Hash(dependencySymbolsBytes)
                });
            }
            if (includePackage)
            {
                lineagePackages.Add(new ReleaseLineagePackage(
                    PackageId,
                    "src/Koan.Test/Koan.Test.csproj",
                    Version)
                {
                    SharedInputs = []
                });
                manifestPackages.Add(new ReleasePackage
                {
                    PackageId = PackageId,
                    Version = Version,
                    ProjectPath = "src/Koan.Test/Koan.Test.csproj",
                    Reason = PackagingConstants.VersionChangedReason,
                    IncludeSymbols = true,
                    ProjectDependencies = includeDependency ? [DependencyPackageId] : [],
                    PackageFile = fixture.PackageFileName,
                    PackageSha256 = Hash(fixture.PackageBytes),
                    SymbolsFile = fixture.SymbolsFileName,
                    SymbolsSha256 = Hash(fixture.SymbolBytes)
                });
            }

            var lineage = new ReleaseLineage
            {
                PreviousSourceCommit = OtherCommit,
                SourceCommit = SourceCommit,
                PreviousVersionCommit = PreviousVersionCommit,
                VersionCommit = VersionCommit,
                IsBootstrap = false,
                Packages = lineagePackages
            };
            var manifest = new ReleaseManifest
            {
                PreviousVersionCommit = PreviousVersionCommit,
                SourceCommit = SourceCommit,
                VersionCommit = VersionCommit,
                IsLineageBootstrap = false,
                CreatedAtUtc = DateTimeOffset.UnixEpoch,
                Packages = manifestPackages
            };
            var firstUse = new FirstUseEvidence(
                "package",
                ".NET",
                "test",
                DateTimeOffset.UnixEpoch,
                1,
                true,
                true,
                "sqlite",
                true,
                true,
                true,
                true,
                true,
                true,
                true,
                [new ApplicationStepEvidence("first-use", 1)]);
            var goldenJourney = new GoldenJourneyEvidence(
                Lane: "package",
                StartedAtUtc: DateTimeOffset.UnixEpoch,
                TotalSeconds: 1,
                CompositionLockfileObserved: true,
                CompositionLockfileMatched: true,
                BusinessRuleObserved: true,
                PersistenceObserved: true,
                ReactiveWorkObserved: true,
                JobsCompositionObserved: true,
                FactsConverged: true,
                CustomMutationSchemaTruthful: true,
                AgentBoundaryObserved: true,
                AgentMutationObserved: true,
                AdapterRejectionExplained: true,
                AdapterRejectionAffectedReadiness: true,
                RejectedWorkerLogsCalm: true,
                AdapterRecoveryObserved: true,
                Steps: [new ApplicationStepEvidence("golden-journey", 1)]);

            await WriteJsonAsync(fixture.LineagePath, lineage);
            await WriteJsonAsync(fixture.ManifestPath, manifest);
            await WriteJsonAsync(
                Path.Combine(fixture.EvidenceDirectory, PackagingConstants.FirstUse.EvidenceFileName),
                firstUse);
            await WriteJsonAsync(
                Path.Combine(fixture.EvidenceDirectory, PackagingConstants.GoldenJourney.EvidenceFileName),
                goldenJourney);
            var marker = await ReleaseWaveBundle.PrepareAsync(
                new ReleaseWavePreparation(
                    fixture.LineagePath,
                    fixture.ManifestPath,
                    fixture.ArtifactDirectory,
                    fixture.EvidenceDirectory,
                    fixture.OutputDirectory,
                    new ReleaseWaveProducer("tests", "1", ".NET", "test")),
                CancellationToken.None);
            fixture.Marker = marker;
            return fixture;
        }

        public ReleaseWaveCoordinator Coordinator(
            FakeEscrow escrow,
            FakePromotionTarget target) =>
            new(escrow, target, ScratchDirectory);

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }

        private static string Hash(byte[] bytes) =>
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        private static Task WriteJsonAsync<T>(string path, T value) =>
            File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions) + "\n");
    }

    private sealed class FakeEscrow : IReleaseWaveEscrow
    {
        private readonly List<StoredRelease> releases = [];
        private readonly Dictionary<string, string> tagTargets = new(StringComparer.Ordinal);
        private int nextId;

        public bool FailCompletionUploadAfterStoreOnce { get; set; }
        public bool FailPublishAfterMutationOnce { get; set; }
        public bool FailCreateAfterMutationOnce { get; set; }
        public bool FailDeleteAfterMutationOnce { get; set; }
        public bool FailBundleUploadAfterStoreOnce { get; set; }
        public bool FailMarkerUploadAfterStoreOnce { get; set; }
        public int CreateCount { get; private set; }
        public int DeleteCount { get; private set; }
        public int PublishCount { get; private set; }
        public List<(string ReleaseId, string AssetName)> UploadAttempts { get; } = [];

        public Task<IReadOnlyList<ReleaseWaveEscrowRelease>> FindByTagIncludingDraftsAsync(
            string tagName,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReleaseWaveEscrowRelease>>(
                releases
                    .Where(release => string.Equals(release.TagName, tagName, StringComparison.Ordinal))
                    .Select(release => release.Snapshot())
                    .ToArray());

        public Task<ReleaseWaveEscrowRelease> CreateDraftAsync(
            string tagName,
            string targetCommit,
            CancellationToken cancellationToken)
        {
            CreateCount++;
            var release = new StoredRelease($"release-{++nextId}", tagName, targetCommit);
            releases.Add(release);
            if (FailCreateAfterMutationOnce)
            {
                FailCreateAfterMutationOnce = false;
                throw new InvalidOperationException("injected create response loss");
            }
            return Task.FromResult(release.Snapshot());
        }

        public Task DeleteDraftAsync(string releaseId, CancellationToken cancellationToken)
        {
            var release = Find(releaseId);
            if (!release.IsDraft) throw new InvalidOperationException("Cannot delete published release.");
            releases.Remove(release);
            DeleteCount++;
            if (FailDeleteAfterMutationOnce)
            {
                FailDeleteAfterMutationOnce = false;
                throw new InvalidOperationException("injected delete response loss");
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ReleaseWaveEscrowAsset>> ListAssetsAsync(
            string releaseId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReleaseWaveEscrowAsset>>(
                Find(releaseId).Assets
                    .Select(asset => new ReleaseWaveEscrowAsset(
                        asset.Name,
                        asset.Bytes.LongLength,
                        asset.IsUploaded))
                    .ToArray());

        public async Task DownloadAssetAsync(
            string releaseId,
            string assetName,
            string destinationPath,
            CancellationToken cancellationToken)
        {
            var asset = Find(releaseId).Assets.Single(item =>
                string.Equals(item.Name, assetName, StringComparison.Ordinal));
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous);
            await destination.WriteAsync(asset.Bytes, cancellationToken);
        }

        public async Task UploadAssetAsync(
            string releaseId,
            string assetName,
            string sourcePath,
            CancellationToken cancellationToken)
        {
            var release = Find(releaseId);
            var existing = release.Assets.SingleOrDefault(asset =>
                string.Equals(asset.Name, assetName, StringComparison.OrdinalIgnoreCase));
            if (existing is not null && existing.IsUploaded)
            {
                throw new InvalidOperationException($"Asset {assetName} already exists.");
            }
            if (existing is not null) release.Assets.Remove(existing);
            var bytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
            release.Assets.Add(new StoredAsset(assetName, bytes, isUploaded: true));
            UploadAttempts.Add((releaseId, assetName));
            if (FailBundleUploadAfterStoreOnce &&
                assetName.EndsWith(PackagingConstants.ReleaseWave.BundleFileExtension, StringComparison.Ordinal))
            {
                FailBundleUploadAfterStoreOnce = false;
                throw new InvalidOperationException("injected bundle upload response loss");
            }
            if (FailMarkerUploadAfterStoreOnce &&
                assetName == PackagingConstants.ReleaseWave.MarkerFileName)
            {
                FailMarkerUploadAfterStoreOnce = false;
                throw new InvalidOperationException("injected marker upload response loss");
            }
            if (FailCompletionUploadAfterStoreOnce && assetName == ReleaseWaveCompletion.FileName)
            {
                FailCompletionUploadAfterStoreOnce = false;
                throw new InvalidOperationException("injected completion upload response loss");
            }
        }

        public Task PublishAsync(
            string releaseId,
            string tagName,
            string versionCommit,
            CancellationToken cancellationToken)
        {
            PublishCount++;
            if (tagTargets.TryGetValue(tagName, out var existing) && existing != versionCommit)
            {
                throw new InvalidOperationException("Tag target conflict.");
            }
            tagTargets[tagName] = versionCommit;
            var release = Find(releaseId);
            release.IsDraft = false;
            release.IsImmutable = true;
            if (FailPublishAfterMutationOnce)
            {
                FailPublishAfterMutationOnce = false;
                throw new InvalidOperationException("injected publish response loss");
            }
            return Task.CompletedTask;
        }

        public Task<string?> ResolveTagTargetAsync(
            string tagName,
            CancellationToken cancellationToken) =>
            Task.FromResult(tagTargets.GetValueOrDefault(tagName));

        public void SeedStarter(string tagName, string targetCommit)
        {
            var release = new StoredRelease($"release-{++nextId}", tagName, targetCommit);
            release.Assets.Add(new StoredAsset(
                PackagingConstants.ReleaseWave.MarkerFileName,
                "starter"u8.ToArray(),
                isUploaded: false));
            releases.Add(release);
        }

        public ReleaseWaveEscrowRelease SingleRelease(string tagName) =>
            releases.Single(release => release.TagName == tagName).Snapshot();

        public bool HasAsset(string tagName, string assetName) =>
            Stored(tagName).Assets.Any(asset => asset.Name == assetName);

        public bool HasUploadedAsset(string tagName, string assetName) =>
            Stored(tagName).Assets.Any(asset =>
                asset.Name == assetName && asset.IsUploaded);

        public byte[] AssetBytes(string tagName, string assetName) =>
            Stored(tagName).Assets.Single(asset => asset.Name == assetName).Bytes.ToArray();

        public void RemoveAsset(string tagName, string assetName) =>
            Stored(tagName).Assets.RemoveAll(asset => asset.Name == assetName);

        public void TamperAsset(string tagName, string assetName)
        {
            var asset = Stored(tagName).Assets.Single(item => item.Name == assetName);
            asset.Bytes[0] ^= 0x7f;
        }

        public void SetReleaseTarget(string tagName, string targetCommit) =>
            Stored(tagName).TargetCommit = targetCommit;

        public void SetTagTarget(string tagName, string targetCommit) =>
            tagTargets[tagName] = targetCommit;

        public void SeedStarterAsset(string tagName, string assetName) =>
            Stored(tagName).Assets.Add(new StoredAsset(
                assetName,
                "starter"u8.ToArray(),
                isUploaded: false));

        private StoredRelease Stored(string tagName) =>
            releases.Single(release => release.TagName == tagName);

        private StoredRelease Find(string releaseId) =>
            releases.Single(release => release.Id == releaseId);

        private sealed class StoredRelease(string id, string tagName, string targetCommit)
        {
            public string Id { get; } = id;
            public string TagName { get; } = tagName;
            public string TargetCommit { get; set; } = targetCommit;
            public bool IsDraft { get; set; } = true;
            public bool IsImmutable { get; set; }
            public List<StoredAsset> Assets { get; } = [];

            public ReleaseWaveEscrowRelease Snapshot() =>
                new(Id, TagName, TargetCommit, IsDraft, IsImmutable);
        }

        private sealed class StoredAsset(string name, byte[] bytes, bool isUploaded)
        {
            public string Name { get; } = name;
            public byte[] Bytes { get; } = bytes;
            public bool IsUploaded { get; } = isUploaded;
        }
    }

    private sealed class FakePromotionTarget : IPackagePromotionTarget
    {
        private readonly Dictionary<string, byte[]> publicPackages = new(StringComparer.OrdinalIgnoreCase);

        public bool FailSymbolsOnce { get; set; }
        public bool FailPackageAfterStoreOnce { get; set; }
        public bool FailWaitOnce { get; set; }
        public List<(string Identity, byte[] Bytes)> PackageAttempts { get; } = [];
        public List<(string Identity, byte[] Bytes)> SymbolAttempts { get; } = [];
        public List<string> Operations { get; } = [];

        public Task<bool> ExistsAsync(
            string packageId,
            string version,
            CancellationToken cancellationToken)
        {
            Operations.Add($"exists:{Identity(packageId, version)}");
            return Task.FromResult(IsPublic(packageId, version));
        }

        public async Task PushPackageAsync(
            string packageId,
            string version,
            string packagePath,
            string expectedSha256,
            CancellationToken cancellationToken)
        {
            var bytes = await File.ReadAllBytesAsync(packagePath, cancellationToken);
            RequireHash(bytes, expectedSha256);
            var identity = Identity(packageId, version);
            PackageAttempts.Add((identity, bytes));
            Operations.Add($"push:{identity}");
            publicPackages.Add(identity, bytes);
            if (FailPackageAfterStoreOnce)
            {
                FailPackageAfterStoreOnce = false;
                throw new InvalidOperationException("injected package push response loss");
            }
        }

        public async Task ReplaySymbolsAsync(
            string packageId,
            string version,
            string symbolsPath,
            string expectedSha256,
            CancellationToken cancellationToken)
        {
            var bytes = await File.ReadAllBytesAsync(symbolsPath, cancellationToken);
            RequireHash(bytes, expectedSha256);
            var identity = Identity(packageId, version);
            SymbolAttempts.Add((identity, bytes));
            Operations.Add($"symbols:{identity}");
            if (FailSymbolsOnce)
            {
                FailSymbolsOnce = false;
                throw new InvalidOperationException("injected symbol failure");
            }
        }

        public Task WaitUntilAvailableAsync(
            string packageId,
            string version,
            CancellationToken cancellationToken)
        {
            var identity = Identity(packageId, version);
            Operations.Add($"wait:{identity}");
            if (!publicPackages.ContainsKey(identity))
            {
                throw new InvalidOperationException($"Package {identity} is not public.");
            }
            if (FailWaitOnce)
            {
                FailWaitOnce = false;
                throw new InvalidOperationException("injected registry wait response loss");
            }
            return Task.CompletedTask;
        }

        public void SeedPublic(string packageId, string version, byte[] bytes) =>
            publicPackages[Identity(packageId, version)] = bytes.ToArray();

        public bool IsPublic(string packageId, string version) =>
            publicPackages.ContainsKey(Identity(packageId, version));

        public void RemovePublic(string packageId, string version) =>
            publicPackages.Remove(Identity(packageId, version));

        private static string Identity(string packageId, string version) =>
            $"{packageId}/{version}";

        private static void RequireHash(byte[] bytes, string expectedSha256)
        {
            var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (actual != expectedSha256)
            {
                throw new InvalidOperationException("Fake target received bytes with a different hash.");
            }
        }
    }
}
