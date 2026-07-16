using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class ReleaseWaveBundleTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly DateTimeOffset CanonicalTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PreparedWaveLoadsAndExtractsTheExactVerifiedInputs()
    {
        using var fixture = await WaveFixture.CreateAsync();
        var output = fixture.CreateOutputDirectory("prepared");

        var marker = await ReleaseWaveBundle.PrepareAsync(fixture.Preparation(output), CancellationToken.None);
        var markerPath = Path.Combine(output, PackagingConstants.ReleaseWave.MarkerFileName);
        var loaded = await ReleaseWaveBundle.LoadAndValidateAsync(markerPath, CancellationToken.None);

        Assert.Equal(PackagingConstants.ReleaseWave.PreparedStatus, loaded.Status);
        Assert.Equal(WaveFixture.VersionCommit, loaded.VersionCommit);
        Assert.Equal($"release/dev/{WaveFixture.VersionCommit}", loaded.TagName);
        Assert.Equal(WaveFixture.VersionCommit, loaded.TagCommit);
        Assert.Equal(1, loaded.PackageCount);

        var extraction = Path.Combine(fixture.Root, "extracted");
        await ReleaseWaveBundle.ExtractAsync(markerPath, extraction, CancellationToken.None);

        var expected = new[]
        {
            PackagingConstants.FirstUse.EvidenceFileName,
            PackagingConstants.GoldenJourney.EvidenceFileName,
            PackagingConstants.LineageArtifactFileName,
            PackagingConstants.ManifestFileName,
            WaveFixture.PackageFileName,
            WaveFixture.SymbolsFileName
        }.Order(StringComparer.Ordinal).ToArray();
        var actual = Directory.EnumerateFiles(extraction)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected, actual);
        Assert.Equal(await File.ReadAllBytesAsync(fixture.PackagePath), await File.ReadAllBytesAsync(Path.Combine(extraction, WaveFixture.PackageFileName)));
        Assert.Equal(await File.ReadAllBytesAsync(fixture.SymbolsPath), await File.ReadAllBytesAsync(Path.Combine(extraction, WaveFixture.SymbolsFileName)));
        Assert.Equal(marker, loaded);
    }

    [Fact]
    public async Task SameInputsProduceByteIdenticalBundleAndMarker()
    {
        using var fixture = await WaveFixture.CreateAsync();
        var firstOutput = fixture.CreateOutputDirectory("repeat-a");
        var secondOutput = fixture.CreateOutputDirectory("repeat-b");

        var first = await ReleaseWaveBundle.PrepareAsync(fixture.Preparation(firstOutput), CancellationToken.None);
        var second = await ReleaseWaveBundle.PrepareAsync(fixture.Preparation(secondOutput), CancellationToken.None);

        Assert.Equal(first.Bundle.Sha256, second.Bundle.Sha256);
        Assert.Equal(first.Bundle.Length, second.Bundle.Length);
        Assert.Equal(
            await File.ReadAllBytesAsync(Path.Combine(firstOutput, first.Bundle.FileName)),
            await File.ReadAllBytesAsync(Path.Combine(secondOutput, second.Bundle.FileName)));
        Assert.Equal(
            await File.ReadAllBytesAsync(Path.Combine(firstOutput, PackagingConstants.ReleaseWave.MarkerFileName)),
            await File.ReadAllBytesAsync(Path.Combine(secondOutput, PackagingConstants.ReleaseWave.MarkerFileName)));
    }

    [Theory]
    [InlineData("missing", "missing")]
    [InlineData("unknown", "not referenced")]
    [InlineData("tampered", "hash mismatch")]
    public async Task PreparationRejectsAnInexactArtifactSetBeforeWritingMarker(string mode, string expectedMessage)
    {
        using var fixture = await WaveFixture.CreateAsync();
        var output = fixture.CreateOutputDirectory($"invalid-{mode}");
        switch (mode)
        {
            case "missing":
                File.Delete(fixture.PackagePath);
                break;
            case "unknown":
                await File.WriteAllTextAsync(Path.Combine(fixture.ArtifactDirectory, "Sylin.Koan.Unknown.1.0.0.nupkg"), "unknown");
                break;
            case "tampered":
                await File.AppendAllTextAsync(fixture.PackagePath, "different bits");
                break;
        }

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReleaseWaveBundle.PrepareAsync(fixture.Preparation(output), CancellationToken.None));

        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(output, PackagingConstants.ReleaseWave.MarkerFileName)));
    }

    [Fact]
    public async Task LoadRejectsBundleTampering()
    {
        using var fixture = await WaveFixture.CreateAsync();
        var output = fixture.CreateOutputDirectory("tamper");
        var marker = await ReleaseWaveBundle.PrepareAsync(fixture.Preparation(output), CancellationToken.None);
        var bundlePath = Path.Combine(output, marker.Bundle.FileName);
        await File.AppendAllTextAsync(bundlePath, "tamper");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReleaseWaveBundle.LoadAndValidateAsync(
                Path.Combine(output, PackagingConstants.ReleaseWave.MarkerFileName),
                CancellationToken.None));

        Assert.Contains("length mismatch", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../escape.nupkg", null, "unsafe")]
    [InlineData("unexpected.nupkg", null, "unknown")]
    [InlineData(null, WaveFixture.PackageFileName, "missing")]
    public async Task LoadRejectsTraversalUnknownAndMissingEntries(
        string? addedEntry,
        string? omittedEntry,
        string expectedMessage)
    {
        using var fixture = await WaveFixture.CreateAsync();
        var output = fixture.CreateOutputDirectory("hostile");
        var marker = await ReleaseWaveBundle.PrepareAsync(fixture.Preparation(output), CancellationToken.None);
        marker = await RewriteBundleAsync(output, marker, addedEntry, omittedEntry);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ReleaseWaveBundle.LoadAndValidateAsync(
                Path.Combine(output, PackagingConstants.ReleaseWave.MarkerFileName),
                CancellationToken.None));

        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<ReleaseWave> RewriteBundleAsync(
        string output,
        ReleaseWave marker,
        string? addedEntry,
        string? omittedEntry)
    {
        var bundlePath = Path.Combine(output, marker.Bundle.FileName);
        List<(string Name, byte[] Bytes)> entries;
        using (var archive = ZipFile.OpenRead(bundlePath))
        {
            entries = archive.Entries
                .Where(entry => !string.Equals(entry.FullName, omittedEntry, StringComparison.Ordinal))
                .Select(entry =>
                {
                    using var source = entry.Open();
                    using var destination = new MemoryStream();
                    source.CopyTo(destination);
                    return (entry.FullName, destination.ToArray());
                })
                .ToList();
        }

        if (addedEntry is not null) entries.Add((addedEntry, "hostile"u8.ToArray()));
        File.Delete(bundlePath);
        using (var archive = ZipFile.Open(bundlePath, ZipArchiveMode.Create))
        {
            foreach (var item in entries.OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                var entry = archive.CreateEntry(item.Name, CompressionLevel.NoCompression);
                entry.LastWriteTime = CanonicalTimestamp;
                entry.ExternalAttributes = 0;
                await using var destination = entry.Open();
                await destination.WriteAsync(item.Bytes);
            }
        }

        marker = marker with
        {
            Bundle = marker.Bundle with
            {
                Length = new FileInfo(bundlePath).Length,
                Sha256 = await HashAsync(bundlePath)
            }
        };
        await File.WriteAllTextAsync(
            Path.Combine(output, PackagingConstants.ReleaseWave.MarkerFileName),
            JsonSerializer.Serialize(marker, JsonOptions) + "\n");
        return marker;
    }

    private static async Task<string> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }

    private sealed class WaveFixture : IDisposable
    {
        public const string PreviousVersionCommit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        public const string SourceCommit = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        public const string VersionCommit = "cccccccccccccccccccccccccccccccccccccccc";
        public const string PackageFileName = "Sylin.Koan.Test.1.2.3.nupkg";
        public const string SymbolsFileName = "Sylin.Koan.Test.1.2.3.snupkg";

        private static readonly ReleaseWaveProducer Producer = new(
            "Koan.Packaging.Tests",
            "1.0.0",
            ".NET test runtime",
            "test operating system");

        private WaveFixture(string root)
        {
            Root = root;
            ArtifactDirectory = Path.Combine(root, "artifacts");
            EvidenceDirectory = Path.Combine(root, "evidence");
            Directory.CreateDirectory(ArtifactDirectory);
            Directory.CreateDirectory(EvidenceDirectory);
            LineagePath = Path.Combine(root, PackagingConstants.LineageArtifactFileName);
            ManifestPath = Path.Combine(root, PackagingConstants.ManifestFileName);
            PackagePath = Path.Combine(ArtifactDirectory, PackageFileName);
            SymbolsPath = Path.Combine(ArtifactDirectory, SymbolsFileName);
        }

        public string Root { get; }
        public string ArtifactDirectory { get; }
        public string EvidenceDirectory { get; }
        public string LineagePath { get; }
        public string ManifestPath { get; }
        public string PackagePath { get; }
        public string SymbolsPath { get; }

        public static async Task<WaveFixture> CreateAsync()
        {
            var fixture = new WaveFixture(Path.Combine(Path.GetTempPath(), $"koan-release-wave-{Guid.NewGuid():N}"));
            Directory.CreateDirectory(fixture.Root);
            await File.WriteAllBytesAsync(fixture.PackagePath, "exact nupkg bytes"u8.ToArray());
            await File.WriteAllBytesAsync(fixture.SymbolsPath, "exact snupkg bytes"u8.ToArray());

            var lineage = new ReleaseLineage
            {
                PreviousSourceCommit = "dddddddddddddddddddddddddddddddddddddddd",
                SourceCommit = SourceCommit,
                PreviousVersionCommit = PreviousVersionCommit,
                VersionCommit = VersionCommit,
                IsBootstrap = false,
                Packages =
                [
                    new ReleaseLineagePackage(
                        "Sylin.Koan.Test",
                        "src/Koan.Test/Koan.Test.csproj",
                        "1.2.3")
                    {
                        SharedInputs = []
                    }
                ]
            };
            var manifest = new ReleaseManifest
            {
                PreviousVersionCommit = PreviousVersionCommit,
                SourceCommit = SourceCommit,
                VersionCommit = VersionCommit,
                IsLineageBootstrap = false,
                CreatedAtUtc = DateTimeOffset.UnixEpoch,
                Packages =
                [
                    new ReleasePackage
                    {
                        PackageId = "Sylin.Koan.Test",
                        Version = "1.2.3",
                        ProjectPath = "src/Koan.Test/Koan.Test.csproj",
                        Kind = "Package",
                        Reason = PackagingConstants.VersionChangedReason,
                        IncludeSymbols = true,
                        PackageFile = PackageFileName,
                        PackageSha256 = await HashAsync(fixture.PackagePath),
                        SymbolsFile = SymbolsFileName,
                        SymbolsSha256 = await HashAsync(fixture.SymbolsPath)
                    }
                ]
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
            await WriteJsonAsync(Path.Combine(fixture.EvidenceDirectory, PackagingConstants.FirstUse.EvidenceFileName), firstUse);
            await WriteJsonAsync(Path.Combine(fixture.EvidenceDirectory, PackagingConstants.GoldenJourney.EvidenceFileName), goldenJourney);
            return fixture;
        }

        public string CreateOutputDirectory(string name)
        {
            var path = Path.Combine(Root, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public ReleaseWavePreparation Preparation(string outputDirectory) =>
            new(LineagePath, ManifestPath, ArtifactDirectory, EvidenceDirectory, outputDirectory, Producer);

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }

        private static async Task WriteJsonAsync<T>(string path, T value) =>
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, JsonOptions) + "\n");
    }
}
