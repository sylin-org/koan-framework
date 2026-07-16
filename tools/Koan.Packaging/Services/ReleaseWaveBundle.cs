using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;

namespace Koan.Packaging.Services;

internal static class ReleaseWaveBundle
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly DateTimeOffset CanonicalEntryTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task<ReleaseWave> PrepareAsync(
        ReleaseWavePreparation preparation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        var outputDirectory = Path.GetFullPath(RequireValue(preparation.OutputDirectory, nameof(preparation.OutputDirectory)));
        Directory.CreateDirectory(outputDirectory);
        var markerPath = Path.Combine(outputDirectory, PackagingConstants.ReleaseWave.MarkerFileName);

        // A prepared marker is the only completion signal. Remove it before touching the bundle so
        // an interrupted replacement can never be mistaken for a complete wave.
        if (File.Exists(markerPath)) File.Delete(markerPath);

        var lineageBytes = await ReadMetadataAsync(preparation.LineagePath, PackagingConstants.LineageArtifactFileName, cancellationToken);
        var manifestBytes = await ReadMetadataAsync(preparation.ManifestPath, PackagingConstants.ManifestFileName, cancellationToken);
        var firstUsePath = Path.Combine(
            Path.GetFullPath(RequireValue(preparation.EvidenceDirectory, nameof(preparation.EvidenceDirectory))),
            PackagingConstants.FirstUse.EvidenceFileName);
        var goldenJourneyPath = Path.Combine(
            Path.GetFullPath(preparation.EvidenceDirectory),
            PackagingConstants.GoldenJourney.EvidenceFileName);
        var firstUseBytes = await ReadMetadataAsync(firstUsePath, PackagingConstants.FirstUse.EvidenceFileName, cancellationToken);
        var goldenJourneyBytes = await ReadMetadataAsync(goldenJourneyPath, PackagingConstants.GoldenJourney.EvidenceFileName, cancellationToken);

        var lineage = Deserialize<ReleaseLineage>(lineageBytes, PackagingConstants.LineageArtifactFileName);
        var manifest = Deserialize<ReleaseManifest>(manifestBytes, PackagingConstants.ManifestFileName);
        ValidateLineageAndManifest(lineage, manifest);
        ValidateEvidence<FirstUseEvidence>(firstUseBytes, PackagingConstants.FirstUse.EvidenceFileName);
        ValidateEvidence<GoldenJourneyEvidence>(goldenJourneyBytes, PackagingConstants.GoldenJourney.EvidenceFileName);

        var entries = new Dictionary<string, BundleInput>(StringComparer.OrdinalIgnoreCase);
        AddInput(entries, BundleInput.FromBytes(PackagingConstants.LineageArtifactFileName, lineageBytes));
        AddInput(entries, BundleInput.FromBytes(PackagingConstants.ManifestFileName, manifestBytes));
        AddInput(entries, BundleInput.FromBytes(PackagingConstants.FirstUse.EvidenceFileName, firstUseBytes));
        AddInput(entries, BundleInput.FromBytes(PackagingConstants.GoldenJourney.EvidenceFileName, goldenJourneyBytes));

        var artifactDirectory = Path.GetFullPath(RequireValue(preparation.ArtifactDirectory, nameof(preparation.ArtifactDirectory)));
        if (!Directory.Exists(artifactDirectory))
        {
            throw new InvalidOperationException($"Release artifact directory does not exist: {artifactDirectory}");
        }

        var referencedArtifacts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in manifest.Packages)
        {
            if (string.IsNullOrWhiteSpace(package.PackageFile))
            {
                throw new InvalidOperationException($"Manifest package {package.Identity} has no package artifact filename.");
            }

            RequireArtifactName(package.PackageFile, PackagingConstants.ReleaseWave.PackageExtension, package.Identity);
            RequireSha256(package.PackageSha256, $"manifest package hash for {package.Identity}");
            AddArtifactInput(entries, referencedArtifacts, artifactDirectory, package.PackageFile, package.PackageSha256!, package.Identity);

            if (package.IncludeSymbols && string.IsNullOrWhiteSpace(package.SymbolsFile))
            {
                throw new InvalidOperationException($"Manifest package {package.Identity} requires symbols but has no symbol artifact filename.");
            }

            if (package.SymbolsFile is null)
            {
                if (package.SymbolsSha256 is not null)
                {
                    throw new InvalidOperationException($"Manifest package {package.Identity} has a symbol hash without a symbol artifact filename.");
                }
                continue;
            }

            RequireArtifactName(package.SymbolsFile, PackagingConstants.ReleaseWave.SymbolsExtension, $"{package.Identity} symbols");
            RequireSha256(package.SymbolsSha256, $"manifest symbol hash for {package.Identity}");
            AddArtifactInput(entries, referencedArtifacts, artifactDirectory, package.SymbolsFile, package.SymbolsSha256!, $"{package.Identity} symbols");
        }

        RequireNoUnknownArtifacts(artifactDirectory, referencedArtifacts);

        var orderedEntries = entries.Values.OrderBy(entry => entry.Name, StringComparer.Ordinal).ToArray();
        var uncompressedLength = orderedEntries.Aggregate(0L, (current, entry) => checked(current + entry.Length));
        if (uncompressedLength >= PackagingConstants.ReleaseWave.MaximumBundleLength)
        {
            throw new InvalidOperationException(
                $"Release-wave inputs total {uncompressedLength} bytes; the bundle must remain smaller than 2 GiB.");
        }

        // Hash every package before archive creation. The write path repeats the proof while copying,
        // closing the file-change window between validation and escrow.
        foreach (var artifact in orderedEntries.Where(entry => entry.Path is not null))
        {
            var actualHash = await HashFileAsync(artifact.Path!, cancellationToken);
            if (!string.Equals(actualHash, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Artifact hash mismatch for {artifact.Description}: manifest records {artifact.Sha256}, file has {actualHash}.");
            }
        }

        var bundleFileName = BundleFileName(manifest.VersionCommit);
        var bundlePath = Path.Combine(outputDirectory, bundleFileName);
        var temporaryBundlePath = Path.Combine(outputDirectory, $".{bundleFileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await WriteBundleAsync(temporaryBundlePath, orderedEntries, cancellationToken);
            var bundleInfo = new FileInfo(temporaryBundlePath);
            if (bundleInfo.Length >= PackagingConstants.ReleaseWave.MaximumBundleLength)
            {
                throw new InvalidOperationException(
                    $"Release-wave bundle is {bundleInfo.Length} bytes; it must remain smaller than 2 GiB.");
            }

            var bundleHash = await HashFileAsync(temporaryBundlePath, cancellationToken);
            File.Move(temporaryBundlePath, bundlePath, overwrite: true);

            var marker = new ReleaseWave
            {
                Status = PackagingConstants.ReleaseWave.PreparedStatus,
                PreviousVersionCommit = manifest.PreviousVersionCommit,
                SourceCommit = manifest.SourceCommit,
                VersionCommit = manifest.VersionCommit,
                TagName = TagName(manifest.VersionCommit),
                TagCommit = manifest.VersionCommit,
                Bundle = new ReleaseWaveFile(bundleFileName, bundleInfo.Length, bundleHash),
                Lineage = new ReleaseWaveFile(
                    PackagingConstants.LineageArtifactFileName,
                    lineageBytes.LongLength,
                    HashBytes(lineageBytes)),
                Manifest = new ReleaseWaveFile(
                    PackagingConstants.ManifestFileName,
                    manifestBytes.LongLength,
                    HashBytes(manifestBytes)),
                PackageCount = manifest.Packages.Count,
                Producer = preparation.Producer ?? CurrentProducer()
            };

            ValidateMarker(marker);
            var markerJson = JsonSerializer.Serialize(marker, JsonOptions) + "\n";
            var temporaryMarkerPath = Path.Combine(outputDirectory, $".{PackagingConstants.ReleaseWave.MarkerFileName}.{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(temporaryMarkerPath, markerJson, new UTF8Encoding(false), cancellationToken);
                File.Move(temporaryMarkerPath, markerPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryMarkerPath)) File.Delete(temporaryMarkerPath);
            }

            return marker;
        }
        finally
        {
            if (File.Exists(temporaryBundlePath)) File.Delete(temporaryBundlePath);
        }
    }

    public static async Task<ReleaseWave> LoadAndValidateAsync(
        string markerPath,
        CancellationToken cancellationToken)
    {
        markerPath = Path.GetFullPath(RequireValue(markerPath, nameof(markerPath)));
        RequireMarkerPath(markerPath);

        var markerBytes = await ReadMetadataAsync(markerPath, PackagingConstants.ReleaseWave.MarkerFileName, cancellationToken);
        var marker = Deserialize<ReleaseWave>(markerBytes, PackagingConstants.ReleaseWave.MarkerFileName);
        ValidateMarker(marker);
        var bundlePath = Path.Combine(Path.GetDirectoryName(markerPath)!, marker.Bundle.FileName);
        await WithValidatedArchiveAsync(marker, bundlePath, null, cancellationToken);
        return marker;
    }

    public static async Task<ReleaseWave> ExtractAsync(
        string markerPath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        markerPath = Path.GetFullPath(RequireValue(markerPath, nameof(markerPath)));
        RequireMarkerPath(markerPath);
        destinationDirectory = Path.GetFullPath(RequireValue(destinationDirectory, nameof(destinationDirectory)));
        if (Directory.Exists(destinationDirectory) || File.Exists(destinationDirectory))
        {
            throw new InvalidOperationException($"Release-wave extraction destination already exists: {destinationDirectory}");
        }

        var markerBytes = await ReadMetadataAsync(markerPath, PackagingConstants.ReleaseWave.MarkerFileName, cancellationToken);
        var marker = Deserialize<ReleaseWave>(markerBytes, PackagingConstants.ReleaseWave.MarkerFileName);
        ValidateMarker(marker);

        var parent = Path.GetDirectoryName(destinationDirectory)
            ?? throw new InvalidOperationException($"Extraction destination has no parent: {destinationDirectory}");
        Directory.CreateDirectory(parent);
        var stagingDirectory = Path.Combine(parent, $".{Path.GetFileName(destinationDirectory)}.{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            var bundlePath = Path.Combine(Path.GetDirectoryName(markerPath)!, marker.Bundle.FileName);
            await WithValidatedArchiveAsync(
                marker,
                bundlePath,
                async archive =>
                {
                    foreach (var entry in archive.Entries)
                    {
                        var destination = Path.Combine(stagingDirectory, entry.FullName);
                        await using var source = entry.Open();
                        await using var target = new FileStream(
                            destination,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None,
                            81920,
                            FileOptions.Asynchronous | FileOptions.SequentialScan);
                        await source.CopyToAsync(target, cancellationToken);
                    }
                },
                cancellationToken);

            Directory.Move(stagingDirectory, destinationDirectory);
            return marker;
        }
        finally
        {
            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    private static async Task WithValidatedArchiveAsync(
        ReleaseWave marker,
        string bundlePath,
        Func<ZipArchive, Task>? afterValidation,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(bundlePath))
        {
            throw new InvalidOperationException($"Prepared release-wave bundle is missing: {bundlePath}");
        }

        await using var bundle = new FileStream(
            bundlePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (bundle.Length != marker.Bundle.Length)
        {
            throw new InvalidOperationException(
                $"Release-wave bundle length mismatch: marker records {marker.Bundle.Length}, file has {bundle.Length}.");
        }
        if (bundle.Length >= PackagingConstants.ReleaseWave.MaximumBundleLength)
        {
            throw new InvalidOperationException("Release-wave bundle must remain smaller than 2 GiB.");
        }

        var bundleHash = Convert.ToHexString(await SHA256.HashDataAsync(bundle, cancellationToken)).ToLowerInvariant();
        if (!string.Equals(bundleHash, marker.Bundle.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Release-wave bundle hash mismatch: marker records {marker.Bundle.Sha256}, file has {bundleHash}.");
        }
        bundle.Position = 0;

        using var archive = new ZipArchive(bundle, ZipArchiveMode.Read, leaveOpen: true, Encoding.UTF8);
        var archiveEntries = archive.Entries.ToArray();
        ValidateArchiveShape(archiveEntries);

        var byName = archiveEntries.ToDictionary(entry => entry.FullName, StringComparer.OrdinalIgnoreCase);
        var lineageEntry = RequireEntry(byName, PackagingConstants.LineageArtifactFileName);
        var manifestEntry = RequireEntry(byName, PackagingConstants.ManifestFileName);
        var lineageBytes = await ReadEntryAsync(lineageEntry, cancellationToken);
        var manifestBytes = await ReadEntryAsync(manifestEntry, cancellationToken);
        RequireInnerFile(marker.Lineage, lineageEntry, lineageBytes, PackagingConstants.LineageArtifactFileName);
        RequireInnerFile(marker.Manifest, manifestEntry, manifestBytes, PackagingConstants.ManifestFileName);

        var lineage = Deserialize<ReleaseLineage>(lineageBytes, PackagingConstants.LineageArtifactFileName);
        var manifest = Deserialize<ReleaseManifest>(manifestBytes, PackagingConstants.ManifestFileName);
        ValidateLineageAndManifest(lineage, manifest);
        RequireMarkerMatchesManifest(marker, manifest);

        var expectedEntries = ExpectedEntryNames(manifest);
        var actualEntries = archiveEntries.Select(entry => entry.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = expectedEntries.Except(actualEntries, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var unknown = actualEntries.Except(expectedEntries, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0 || unknown.Length > 0)
        {
            throw new InvalidOperationException(
                $"Release-wave bundle entry set is not exact. Missing: {Display(missing)}. Unknown: {Display(unknown)}.");
        }

        foreach (var package in manifest.Packages)
        {
            await RequireEntryHashAsync(byName[package.PackageFile!], package.PackageSha256!, package.Identity, cancellationToken);
            if (package.SymbolsFile is not null)
            {
                await RequireEntryHashAsync(byName[package.SymbolsFile], package.SymbolsSha256!, $"{package.Identity} symbols", cancellationToken);
            }
        }

        ValidateEvidence<FirstUseEvidence>(
            await ReadEntryAsync(byName[PackagingConstants.FirstUse.EvidenceFileName], cancellationToken),
            PackagingConstants.FirstUse.EvidenceFileName);
        ValidateEvidence<GoldenJourneyEvidence>(
            await ReadEntryAsync(byName[PackagingConstants.GoldenJourney.EvidenceFileName], cancellationToken),
            PackagingConstants.GoldenJourney.EvidenceFileName);

        if (afterValidation is not null) await afterValidation(archive);
    }

    private static void ValidateArchiveShape(IReadOnlyList<ZipArchiveEntry> entries)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            RequireSafeEntryName(entry.FullName, "bundle entry");
            if (!names.Add(entry.FullName))
            {
                throw new InvalidOperationException($"Release-wave bundle contains a duplicate or case-aliased entry '{entry.FullName}'.");
            }
            // ZIP stores DOS wall-clock fields without an offset. Compare the encoded components,
            // not the local offset reconstructed by the reading machine.
            if (entry.LastWriteTime.DateTime != CanonicalEntryTimestamp.DateTime)
            {
                throw new InvalidOperationException($"Release-wave bundle entry '{entry.FullName}' has a noncanonical timestamp.");
            }
            if (entry.ExternalAttributes != 0)
            {
                throw new InvalidOperationException($"Release-wave bundle entry '{entry.FullName}' has noncanonical external attributes.");
            }
            if (entry.CompressedLength != entry.Length)
            {
                throw new InvalidOperationException($"Release-wave bundle entry '{entry.FullName}' is compressed; escrow entries must be stored exactly.");
            }
        }

        var actualOrder = entries.Select(entry => entry.FullName).ToArray();
        var canonicalOrder = actualOrder.Order(StringComparer.Ordinal).ToArray();
        if (!actualOrder.SequenceEqual(canonicalOrder, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Release-wave bundle entries are not in canonical ordinal order.");
        }
    }

    private static HashSet<string> ExpectedEntryNames(ReleaseManifest manifest)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            PackagingConstants.LineageArtifactFileName,
            PackagingConstants.ManifestFileName,
            PackagingConstants.FirstUse.EvidenceFileName,
            PackagingConstants.GoldenJourney.EvidenceFileName
        };

        foreach (var package in manifest.Packages)
        {
            if (string.IsNullOrWhiteSpace(package.PackageFile) || !names.Add(package.PackageFile))
            {
                throw new InvalidOperationException($"Manifest package {package.Identity} has a missing or duplicate package artifact filename.");
            }
            RequireArtifactName(package.PackageFile, PackagingConstants.ReleaseWave.PackageExtension, package.Identity);
            RequireSha256(package.PackageSha256, $"manifest package hash for {package.Identity}");

            if (package.IncludeSymbols && string.IsNullOrWhiteSpace(package.SymbolsFile))
            {
                throw new InvalidOperationException($"Manifest package {package.Identity} requires symbols but has no symbol artifact filename.");
            }
            if (package.SymbolsFile is null)
            {
                if (package.SymbolsSha256 is not null)
                {
                    throw new InvalidOperationException($"Manifest package {package.Identity} has a symbol hash without a symbol artifact filename.");
                }
                continue;
            }

            RequireArtifactName(package.SymbolsFile, PackagingConstants.ReleaseWave.SymbolsExtension, $"{package.Identity} symbols");
            RequireSha256(package.SymbolsSha256, $"manifest symbol hash for {package.Identity}");
            if (!names.Add(package.SymbolsFile))
            {
                throw new InvalidOperationException($"Manifest package {package.Identity} has a duplicate symbol artifact filename '{package.SymbolsFile}'.");
            }
        }

        return names;
    }

    private static ZipArchiveEntry RequireEntry(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        string name) =>
        entries.TryGetValue(name, out var entry)
            ? entry
            : throw new InvalidOperationException($"Release-wave bundle is missing required entry '{name}'.");

    private static void ValidateLineageAndManifest(ReleaseLineage lineage, ReleaseManifest manifest)
    {
        if (lineage.SchemaVersion != PackagingConstants.ReleaseLineageSchema)
        {
            throw new InvalidOperationException(
                $"Release lineage uses schema {lineage.SchemaVersion}; expected {PackagingConstants.ReleaseLineageSchema}.");
        }
        if (manifest.SchemaVersion != PackagingConstants.ReleaseManifestSchema)
        {
            throw new InvalidOperationException(
                $"Release manifest uses schema {manifest.SchemaVersion}; expected {PackagingConstants.ReleaseManifestSchema}.");
        }

        RequireCommit(lineage.PreviousVersionCommit, "lineage previous version commit");
        RequireCommit(lineage.SourceCommit, "lineage source commit");
        RequireCommit(lineage.VersionCommit, "lineage version commit");
        RequireCommit(manifest.PreviousVersionCommit, "manifest previous version commit");
        RequireCommit(manifest.SourceCommit, "manifest source commit");
        RequireCommit(manifest.VersionCommit, "manifest version commit");

        RequireEqual(lineage.PreviousVersionCommit, manifest.PreviousVersionCommit, "previous version commit");
        RequireEqual(lineage.SourceCommit, manifest.SourceCommit, "source commit");
        RequireEqual(lineage.VersionCommit, manifest.VersionCommit, "version commit");
        if (lineage.IsBootstrap != manifest.IsLineageBootstrap)
        {
            throw new InvalidOperationException(
                $"Release lineage and manifest disagree on bootstrap state: " +
                $"'{lineage.IsBootstrap}' versus '{manifest.IsLineageBootstrap}'.");
        }

        var activeLineage = new Dictionary<string, ReleaseLineagePackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in lineage.Packages)
        {
            RequirePackageField(package.PackageId, "lineage package ID");
            RequirePackageField(package.ProjectPath, $"lineage project path for {package.PackageId}");
            RequirePackageField(package.Version, $"lineage version for {package.PackageId}");
            if (!activeLineage.TryAdd(package.PackageId, package))
            {
                throw new InvalidOperationException(
                    $"Release lineage contains duplicate or case-aliased active package '{package.PackageId}'.");
            }
        }

        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in manifest.Packages)
        {
            RequirePackageField(package.PackageId, "manifest package ID");
            RequirePackageField(package.ProjectPath, $"manifest project path for {package.PackageId}");
            RequirePackageField(package.Version, $"manifest version for {package.PackageId}");
            if (!identities.Add(package.Identity))
            {
                throw new InvalidOperationException($"Release manifest contains duplicate package identity '{package.Identity}'.");
            }

            if (!activeLineage.TryGetValue(package.PackageId, out var recorded))
            {
                throw new InvalidOperationException(
                    $"Release manifest package '{package.Identity}' is not present in the active committed lineage.");
            }
            RequireEqual(recorded.PackageId, package.PackageId, $"package ID for {package.Identity}");
            RequireEqual(recorded.ProjectPath, package.ProjectPath, $"project path for {package.Identity}");
            RequireEqual(recorded.Version!, package.Version, $"version for {package.PackageId}");
        }
    }

    private static void RequirePackageField(string? value, string description)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Release-wave {description} is required.");
        }
    }

    private static void RequireMarkerMatchesManifest(ReleaseWave marker, ReleaseManifest manifest)
    {
        RequireEqual(marker.PreviousVersionCommit, manifest.PreviousVersionCommit, "prepared previous version commit");
        RequireEqual(marker.SourceCommit, manifest.SourceCommit, "prepared source commit");
        RequireEqual(marker.VersionCommit, manifest.VersionCommit, "prepared version commit");
        if (marker.PackageCount != manifest.Packages.Count)
        {
            throw new InvalidOperationException(
                $"Prepared marker records {marker.PackageCount} packages, but the manifest contains {manifest.Packages.Count}.");
        }
    }

    private static void ValidateMarker(ReleaseWave marker)
    {
        if (marker.SchemaVersion != PackagingConstants.ReleaseWave.Schema)
        {
            throw new InvalidOperationException(
                $"Release-wave marker uses schema {marker.SchemaVersion}; expected {PackagingConstants.ReleaseWave.Schema}.");
        }
        if (!string.Equals(marker.Status, PackagingConstants.ReleaseWave.PreparedStatus, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Release-wave marker status must be '{PackagingConstants.ReleaseWave.PreparedStatus}'.");
        }

        RequireCommit(marker.PreviousVersionCommit, "prepared previous version commit");
        RequireCommit(marker.SourceCommit, "prepared source commit");
        RequireCommit(marker.VersionCommit, "prepared version commit");
        RequireCommit(marker.TagCommit, "prepared tag commit");
        RequireEqual(marker.VersionCommit, marker.TagCommit, "prepared tag target");

        var expectedTag = TagName(marker.VersionCommit);
        if (!string.Equals(marker.TagName, expectedTag, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Prepared tag is '{marker.TagName}', expected '{expectedTag}'.");
        }

        var expectedBundle = BundleFileName(marker.VersionCommit);
        if (!string.Equals(marker.Bundle.FileName, expectedBundle, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Prepared bundle is '{marker.Bundle.FileName}', expected '{expectedBundle}'.");
        }
        RequireSafeEntryName(marker.Bundle.FileName, "prepared bundle filename");
        RequireWaveFile(marker.Bundle, "prepared bundle", maximumMetadata: false);

        if (!string.Equals(marker.Lineage.FileName, PackagingConstants.LineageArtifactFileName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Prepared marker has a noncanonical lineage filename.");
        }
        if (!string.Equals(marker.Manifest.FileName, PackagingConstants.ManifestFileName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Prepared marker has a noncanonical manifest filename.");
        }
        RequireWaveFile(marker.Lineage, "prepared lineage", maximumMetadata: true);
        RequireWaveFile(marker.Manifest, "prepared manifest", maximumMetadata: true);

        if (marker.PackageCount < 0) throw new InvalidOperationException("Prepared package count cannot be negative.");
        if (string.IsNullOrWhiteSpace(marker.Producer.Name) ||
            string.IsNullOrWhiteSpace(marker.Producer.Version) ||
            string.IsNullOrWhiteSpace(marker.Producer.Runtime) ||
            string.IsNullOrWhiteSpace(marker.Producer.OperatingSystem))
        {
            throw new InvalidOperationException("Prepared marker producer metadata is incomplete.");
        }
    }

    private static void RequireWaveFile(ReleaseWaveFile file, string description, bool maximumMetadata)
    {
        if (file.Length <= 0) throw new InvalidOperationException($"{description} length must be positive.");
        var maximum = maximumMetadata
            ? PackagingConstants.ReleaseWave.MaximumMetadataEntryLength
            : PackagingConstants.ReleaseWave.MaximumBundleLength;
        if (file.Length >= maximum) throw new InvalidOperationException($"{description} length is outside the supported bound.");
        RequireSha256(file.Sha256, $"{description} hash");
    }

    private static void RequireInnerFile(
        ReleaseWaveFile expected,
        ZipArchiveEntry entry,
        byte[] bytes,
        string description)
    {
        if (entry.Length != expected.Length || bytes.LongLength != expected.Length)
        {
            throw new InvalidOperationException(
                $"Bundled {description} length mismatch: marker records {expected.Length}, entry has {entry.Length}.");
        }
        var hash = HashBytes(bytes);
        if (!string.Equals(hash, expected.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Bundled {description} hash mismatch: marker records {expected.Sha256}, entry has {hash}.");
        }
    }

    private static async Task RequireEntryHashAsync(
        ZipArchiveEntry entry,
        string expectedHash,
        string identity,
        CancellationToken cancellationToken)
    {
        await using var stream = entry.Open();
        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Bundled artifact hash mismatch for {identity}: manifest records {expectedHash}, entry has {actualHash}.");
        }
    }

    private static void AddArtifactInput(
        IDictionary<string, BundleInput> entries,
        ISet<string> referencedArtifacts,
        string artifactDirectory,
        string fileName,
        string expectedHash,
        string identity)
    {
        if (!referencedArtifacts.Add(fileName))
        {
            throw new InvalidOperationException($"Manifest reuses artifact filename '{fileName}' for {identity}.");
        }

        var path = Path.Combine(artifactDirectory, fileName);
        if (!File.Exists(path)) throw new InvalidOperationException($"Manifest artifact for {identity} is missing: {path}");
        var info = new FileInfo(path);
        AddInput(entries, BundleInput.FromFile(fileName, path, info.Length, expectedHash, identity));
    }

    private static void AddInput(IDictionary<string, BundleInput> entries, BundleInput input)
    {
        RequireSafeEntryName(input.Name, "bundle input");
        if (!entries.TryAdd(input.Name, input))
        {
            throw new InvalidOperationException($"Release-wave inputs contain duplicate or case-aliased entry '{input.Name}'.");
        }
    }

    private static void RequireNoUnknownArtifacts(string artifactDirectory, ISet<string> referencedArtifacts)
    {
        var candidates = Directory.EnumerateFiles(artifactDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(path =>
                path.EndsWith(PackagingConstants.ReleaseWave.PackageExtension, StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(PackagingConstants.ReleaseWave.SymbolsExtension, StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetFileName(path)!)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var unknown = candidates
            .Where(file => !referencedArtifacts.Contains(file))
            .ToArray();
        if (unknown.Length > 0)
        {
            throw new InvalidOperationException(
                $"Release artifact directory contains package files not referenced by the manifest: {string.Join(", ", unknown)}.");
        }
    }

    private static async Task WriteBundleAsync(
        string path,
        IReadOnlyList<BundleInput> inputs,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8);
        foreach (var input in inputs)
        {
            var entry = archive.CreateEntry(input.Name, CompressionLevel.NoCompression);
            entry.LastWriteTime = CanonicalEntryTimestamp;
            entry.ExternalAttributes = 0;
            await using var destination = entry.Open();
            if (input.Bytes is not null)
            {
                await destination.WriteAsync(input.Bytes, cancellationToken);
                continue;
            }

            await using var source = new FileStream(
                input.Path!,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long length = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                length = checked(length + read);
            }

            var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (length != input.Length || !string.Equals(actualHash, input.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Artifact for {input.Description} changed while the release-wave bundle was being written.");
            }
        }
    }

    private static async Task<byte[]> ReadMetadataAsync(
        string path,
        string description,
        CancellationToken cancellationToken)
    {
        path = Path.GetFullPath(RequireValue(path, description));
        if (!File.Exists(path)) throw new InvalidOperationException($"Required release-wave input is missing: {path}");
        var info = new FileInfo(path);
        if (info.Length <= 0 || info.Length >= PackagingConstants.ReleaseWave.MaximumMetadataEntryLength)
        {
            throw new InvalidOperationException(
                $"Release-wave metadata '{description}' must be nonempty and smaller than {PackagingConstants.ReleaseWave.MaximumMetadataEntryLength} bytes.");
        }
        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Length <= 0 || entry.Length >= PackagingConstants.ReleaseWave.MaximumMetadataEntryLength)
        {
            throw new InvalidOperationException(
                $"Release-wave metadata entry '{entry.FullName}' must be nonempty and smaller than {PackagingConstants.ReleaseWave.MaximumMetadataEntryLength} bytes.");
        }
        await using var source = entry.Open();
        using var destination = new MemoryStream(checked((int)entry.Length));
        await source.CopyToAsync(destination, cancellationToken);
        return destination.ToArray();
    }

    private static T Deserialize<T>(byte[] bytes, string description) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, JsonOptions)
                ?? throw new InvalidOperationException($"Unable to deserialize '{description}'.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Invalid release-wave JSON in '{description}': {exception.Message}", exception);
        }
    }

    private static void ValidateEvidence<T>(byte[] bytes, string fileName) where T : class
    {
        _ = Deserialize<T>(bytes, fileName);
    }

    private static void RequireArtifactName(string fileName, string extension, string identity)
    {
        RequireSafeEntryName(fileName, $"manifest artifact for {identity}");
        if (!fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Manifest artifact '{fileName}' for {identity} must end in '{extension}'.");
        }
    }

    private static void RequireSafeEntryName(string value, string description)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value is "." or ".." ||
            value != value.Normalize(NormalizationForm.FormC) ||
            value.Any(character =>
                !(character is >= 'a' and <= 'z') &&
                !(character is >= 'A' and <= 'Z') &&
                !(character is >= '0' and <= '9') &&
                character is not '.' and not '-' and not '_' and not '+') ||
            Encoding.UTF8.GetByteCount(value) > ushort.MaxValue)
        {
            throw new InvalidOperationException($"Unsafe or noncanonical {description} name '{value}'.");
        }
    }

    private static void RequireMarkerPath(string markerPath)
    {
        if (!string.Equals(Path.GetFileName(markerPath), PackagingConstants.ReleaseWave.MarkerFileName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Release-wave marker must be named '{PackagingConstants.ReleaseWave.MarkerFileName}'.");
        }
    }

    private static void RequireCommit(string value, string description)
    {
        if (value.Length != 40 || value.Any(character => !(character is >= '0' and <= '9') && !(character is >= 'a' and <= 'f')))
        {
            throw new InvalidOperationException($"{description} must be a full lowercase 40-character Git commit.");
        }
    }

    private static void RequireSha256(string? value, string description)
    {
        if (value is null || value.Length != 64 || value.Any(character => !(character is >= '0' and <= '9') && !(character is >= 'a' and <= 'f')))
        {
            throw new InvalidOperationException($"{description} must be a lowercase SHA-256 value.");
        }
    }

    private static void RequireEqual(string first, string second, string description)
    {
        if (!string.Equals(first, second, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Release lineage and manifest disagree on {description}: '{first}' versus '{second}'.");
        }
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static string HashBytes(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string BundleFileName(string versionCommit) =>
        $"{PackagingConstants.ReleaseWave.BundleFilePrefix}{versionCommit}{PackagingConstants.ReleaseWave.BundleFileExtension}";

    private static string TagName(string versionCommit) =>
        $"{PackagingConstants.ReleaseWave.TagPrefix}{versionCommit}";

    private static ReleaseWaveProducer CurrentProducer()
    {
        var assembly = typeof(ReleaseWaveBundle).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
        return new ReleaseWaveProducer(
            assembly.GetName().Name ?? "Koan.Packaging",
            version,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription);
    }

    private static string RequireValue(string value, string description) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{description} is required.", description)
            : value;

    private static string Display(IReadOnlyList<string> values) =>
        values.Count == 0 ? "none" : string.Join(", ", values);

    private sealed record BundleInput(
        string Name,
        string? Path,
        byte[]? Bytes,
        long Length,
        string Sha256,
        string Description)
    {
        public static BundleInput FromBytes(string name, byte[] bytes) =>
            new(name, null, bytes, bytes.LongLength, HashBytes(bytes), name);

        public static BundleInput FromFile(
            string name,
            string path,
            long length,
            string sha256,
            string description) =>
            new(name, path, null, length, sha256, description);
    }
}
