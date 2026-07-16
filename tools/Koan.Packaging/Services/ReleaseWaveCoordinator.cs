using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;

namespace Koan.Packaging.Services;

internal sealed class ReleaseWaveCoordinator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly IReleaseWaveEscrow escrow;
    private readonly IPackagePromotionTarget promotionTarget;
    private readonly string scratchRoot;

    public ReleaseWaveCoordinator(
        IReleaseWaveEscrow escrow,
        IPackagePromotionTarget promotionTarget,
        string scratchRoot)
    {
        this.escrow = escrow ?? throw new ArgumentNullException(nameof(escrow));
        this.promotionTarget = promotionTarget ?? throw new ArgumentNullException(nameof(promotionTarget));
        if (string.IsNullOrWhiteSpace(scratchRoot))
        {
            throw new ArgumentException("A release-wave scratch directory is required.", nameof(scratchRoot));
        }
        this.scratchRoot = Path.GetFullPath(scratchRoot);
    }

    public async Task<ReleaseWaveInspection> InspectAsync(
        string versionCommit,
        CancellationToken cancellationToken)
    {
        versionCommit = RequireCommit(versionCommit, nameof(versionCommit));
        var tagName = TagName(versionCommit);
        await RequireTagAbsentOrExactAsync(tagName, versionCommit, cancellationToken);
        var release = await FindSingleReleaseAsync(tagName, cancellationToken);
        if (release is null)
        {
            return new ReleaseWaveInspection(
                ReleaseWaveEscrowState.Missing,
                tagName,
                versionCommit,
                null);
        }

        RequireReleaseIdentity(release, tagName, versionCommit);
        var operationDirectory = CreateOperationDirectory("inspect");
        try
        {
            var evaluation = await EvaluateRemoteAsync(
                release,
                versionCommit,
                operationDirectory,
                cancellationToken);
            if (evaluation.Wave is null)
            {
                return new ReleaseWaveInspection(
                    ReleaseWaveEscrowState.Staging,
                    tagName,
                    versionCommit,
                    release.Id);
            }

            if (release.IsDraft)
            {
                return new ReleaseWaveInspection(
                    ReleaseWaveEscrowState.Prepared,
                    tagName,
                    versionCommit,
                    release.Id);
            }

            await RequirePublishedTerminalAsync(
                release,
                evaluation.Wave,
                tagName,
                versionCommit,
                cancellationToken);
            return new ReleaseWaveInspection(
                ReleaseWaveEscrowState.Published,
                tagName,
                versionCommit,
                release.Id);
        }
        finally
        {
            DeleteOperationDirectory(operationDirectory);
        }
    }

    public async Task<ReleaseWaveInspection> StageAsync(
        string localMarkerPath,
        CancellationToken cancellationToken)
    {
        var operationDirectory = CreateOperationDirectory("stage");
        try
        {
            // Snapshot and revalidate both local files before reading or mutating any remote state.
            var local = await LoadLocalAsync(localMarkerPath, operationDirectory, cancellationToken);
            var versionCommit = local.Marker.VersionCommit;
            var tagName = TagName(versionCommit);
            await RequireTagAbsentOrExactAsync(tagName, versionCommit, cancellationToken);
            var release = await FindSingleReleaseAsync(tagName, cancellationToken);
            if (release is not null)
            {
                RequireReleaseIdentity(release, tagName, versionCommit);
                var evaluation = await EvaluateRemoteAsync(
                    release,
                    versionCommit,
                    Path.Combine(operationDirectory, "existing"),
                    cancellationToken);
                if (evaluation.Wave is not null)
                {
                    RequireSamePreparedBytes(local, evaluation.Wave);
                    if (!release.IsDraft)
                    {
                        await RequirePublishedTerminalAsync(
                            release,
                            evaluation.Wave,
                            tagName,
                            versionCommit,
                            cancellationToken);
                        return new ReleaseWaveInspection(
                            ReleaseWaveEscrowState.Published,
                            tagName,
                            versionCommit,
                            release.Id);
                    }

                    return new ReleaseWaveInspection(
                        ReleaseWaveEscrowState.Prepared,
                        tagName,
                        versionCommit,
                        release.Id);
                }

                if (await AnyPackageIsPublicAsync(local.Manifest, cancellationToken))
                {
                    throw new InvalidOperationException(
                        $"Release wave {tagName} has public package identity but no exact prepared escrow. " +
                        "The incomplete draft cannot be reset or rebuilt.");
                }

                await escrow.DeleteDraftAsync(release.Id, cancellationToken);
            }
            else if (await AnyPackageIsPublicAsync(local.Manifest, cancellationToken))
            {
                throw new InvalidOperationException(
                    $"Release wave {tagName} has public package identity but no prepared escrow. " +
                    "Refusing to rebuild different bytes under an existing identity.");
            }

            var draft = await escrow.CreateDraftAsync(tagName, versionCommit, cancellationToken);
            RequireReleaseIdentity(draft, tagName, versionCommit);
            if (!draft.IsDraft)
            {
                throw new InvalidOperationException(
                    $"Escrow creation for {tagName} did not return a draft Release.");
            }

            // The marker is the prepared authority, so it is intentionally uploaded last.
            await escrow.UploadAssetAsync(
                draft.Id,
                local.Marker.Bundle.FileName,
                local.BundlePath,
                cancellationToken);
            await escrow.UploadAssetAsync(
                draft.Id,
                PackagingConstants.ReleaseWave.MarkerFileName,
                local.MarkerPath,
                cancellationToken);

            var staged = await FindSingleReleaseAsync(tagName, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Draft Release {tagName} disappeared after staging.");
            RequireReleaseIdentity(staged, tagName, versionCommit);
            if (!staged.IsDraft)
            {
                throw new InvalidOperationException(
                    $"Release {tagName} became published before package promotion completed.");
            }

            var stagedEvaluation = await EvaluateRemoteAsync(
                staged,
                versionCommit,
                Path.Combine(operationDirectory, "staged"),
                cancellationToken);
            var stagedWave = stagedEvaluation.Wave
                ?? throw new InvalidOperationException(
                    $"Draft Release {tagName} is still incomplete after staging: {stagedEvaluation.Problem}");
            RequireSamePreparedBytes(local, stagedWave);
            return new ReleaseWaveInspection(
                ReleaseWaveEscrowState.Prepared,
                tagName,
                versionCommit,
                staged.Id);
        }
        finally
        {
            DeleteOperationDirectory(operationDirectory);
        }
    }

    public async Task<ReleaseWaveInspection> PromoteAsync(
        string versionCommit,
        CancellationToken cancellationToken)
    {
        versionCommit = RequireCommit(versionCommit, nameof(versionCommit));
        var tagName = TagName(versionCommit);
        await RequireTagAbsentOrExactAsync(tagName, versionCommit, cancellationToken);
        var release = await FindSingleReleaseAsync(tagName, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Release wave {tagName} has not been staged.");
        RequireReleaseIdentity(release, tagName, versionCommit);

        var operationDirectory = CreateOperationDirectory("promote");
        try
        {
            var evaluation = await EvaluateRemoteAsync(
                release,
                versionCommit,
                operationDirectory,
                cancellationToken);
            var wave = evaluation.Wave;
            if (wave is null)
            {
                throw new InvalidOperationException(
                    $"Release wave {tagName} is not exactly prepared: {evaluation.Problem}");
            }

            if (!release.IsDraft)
            {
                await RequirePublishedTerminalAsync(
                    release,
                    wave,
                    tagName,
                    versionCommit,
                    cancellationToken);
                return new ReleaseWaveInspection(
                    ReleaseWaveEscrowState.Published,
                    tagName,
                    versionCommit,
                    release.Id);
            }

            // The manifest is already dependency ordered. Every recovery reuses these exact files;
            // public nupkgs are skipped, while every symbols file is deliberately replayed.
            foreach (var package in wave.Manifest.Packages)
            {
                var packagePath = Path.Combine(wave.ExtractionDirectory, package.PackageFile!);
                if (!await promotionTarget.ExistsAsync(
                        package.PackageId,
                        package.Version,
                        cancellationToken))
                {
                    await promotionTarget.PushPackageAsync(
                        package.PackageId,
                        package.Version,
                        packagePath,
                        package.PackageSha256!,
                        cancellationToken);
                }

                if (package.SymbolsFile is not null)
                {
                    await promotionTarget.ReplaySymbolsAsync(
                        package.PackageId,
                        package.Version,
                        Path.Combine(wave.ExtractionDirectory, package.SymbolsFile),
                        package.SymbolsSha256!,
                        cancellationToken);
                }
            }

            foreach (var package in wave.Manifest.Packages)
            {
                await promotionTarget.WaitUntilAvailableAsync(
                    package.PackageId,
                    package.Version,
                    cancellationToken);
            }

            if (!wave.HasCompletion)
            {
                await escrow.UploadAssetAsync(
                    release.Id,
                    ReleaseWaveCompletion.FileName,
                    wave.CompletionPath,
                    cancellationToken);
            }

            // Re-read the complete remote escrow immediately before the tag/publication boundary.
            // A mutable draft is not terminal authority; this closes accidental asset drift between
            // promotion and publication as far as the GitHub API boundary permits.
            var readyEvaluation = await EvaluateRemoteAsync(
                release,
                versionCommit,
                Path.Combine(operationDirectory, "ready-to-publish"),
                cancellationToken);
            var readyWave = readyEvaluation.Wave
                ?? throw new InvalidOperationException(
                    $"Release wave {tagName} lost prepared authority before publication: {readyEvaluation.Problem}");
            RequireSamePreparedBytes(wave, readyWave);
            if (!readyWave.HasCompletion)
            {
                throw new InvalidOperationException(
                    $"Release wave {tagName} has no exact completion receipt at the publication boundary.");
            }

            await escrow.PublishAsync(
                release.Id,
                tagName,
                versionCommit,
                cancellationToken);

            var published = await FindSingleReleaseAsync(tagName, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Release {tagName} disappeared after publication.");
            RequireReleaseIdentity(published, tagName, versionCommit);
            if (published.IsDraft)
            {
                throw new InvalidOperationException(
                    $"Release {tagName} remained a draft after publication.");
            }

            var publishedEvaluation = await EvaluateRemoteAsync(
                published,
                versionCommit,
                Path.Combine(operationDirectory, "published"),
                cancellationToken);
            var publishedWave = publishedEvaluation.Wave
                ?? throw new InvalidOperationException(
                    $"Published Release {tagName} is incomplete: {publishedEvaluation.Problem}");
            await RequirePublishedTerminalAsync(
                published,
                publishedWave,
                tagName,
                versionCommit,
                cancellationToken);
            return new ReleaseWaveInspection(
                ReleaseWaveEscrowState.Published,
                tagName,
                versionCommit,
                published.Id);
        }
        finally
        {
            DeleteOperationDirectory(operationDirectory);
        }
    }

    private async Task<RemoteWaveEvaluation> EvaluateRemoteAsync(
        ReleaseWaveEscrowRelease release,
        string versionCommit,
        string operationDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(operationDirectory);
        var assets = await escrow.ListAssetsAsync(release.Id, cancellationToken);
        var hasUploadedMarker = assets.Any(asset =>
            asset.IsUploaded &&
            string.Equals(
                asset.Name,
                PackagingConstants.ReleaseWave.MarkerFileName,
                StringComparison.Ordinal));
        var duplicate = assets
            .GroupBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            return InvalidRemote(
                release,
                hasUploadedMarker,
                $"duplicate or case-aliased asset '{duplicate.Key}'");
        }

        var starterAssets = assets.Where(asset => !asset.IsUploaded).ToArray();
        var recoverableCompletionStarter =
            hasUploadedMarker &&
            starterAssets.Length == 1 &&
            string.Equals(
                starterAssets[0].Name,
                ReleaseWaveCompletion.FileName,
                StringComparison.Ordinal);
        if (starterAssets.Length > 0 && !recoverableCompletionStarter)
        {
            return InvalidRemote(
                release,
                hasUploadedMarker,
                "one or more assets are still in the GitHub starter state");
        }

        var expectedBundleName = BundleName(versionCommit);
        var allowedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            PackagingConstants.ReleaseWave.MarkerFileName,
            expectedBundleName,
            ReleaseWaveCompletion.FileName
        };
        var unknown = assets
            .Where(asset => !allowedNames.Contains(asset.Name))
            .Select(asset => asset.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unknown.Length > 0)
        {
            return InvalidRemote(
                release,
                hasUploadedMarker,
                $"unexpected asset(s): {string.Join(", ", unknown)}");
        }

        var byName = assets
            .Where(asset => asset.IsUploaded)
            .ToDictionary(asset => asset.Name, StringComparer.Ordinal);
        if (!byName.TryGetValue(PackagingConstants.ReleaseWave.MarkerFileName, out var markerAsset) ||
            !byName.TryGetValue(expectedBundleName, out var bundleAsset))
        {
            return InvalidRemote(
                release,
                hasUploadedMarker,
                "prepared marker or bundle is missing");
        }
        if (markerAsset.Length <= 0 ||
            markerAsset.Length >= PackagingConstants.ReleaseWave.MaximumMetadataEntryLength ||
            bundleAsset.Length <= 0 ||
            bundleAsset.Length >= PackagingConstants.ReleaseWave.MaximumBundleLength)
        {
            return InvalidRemote(
                release,
                hasUploadedMarker,
                "prepared marker or bundle has an invalid length");
        }

        var filesDirectory = Path.Combine(operationDirectory, "files");
        Directory.CreateDirectory(filesDirectory);
        var markerPath = Path.Combine(filesDirectory, PackagingConstants.ReleaseWave.MarkerFileName);
        var bundlePath = Path.Combine(filesDirectory, expectedBundleName);
        await escrow.DownloadAssetAsync(
            release.Id,
            PackagingConstants.ReleaseWave.MarkerFileName,
            markerPath,
            cancellationToken);
        await escrow.DownloadAssetAsync(
            release.Id,
            expectedBundleName,
            bundlePath,
            cancellationToken);

        if (new FileInfo(markerPath).Length != markerAsset.Length ||
            new FileInfo(bundlePath).Length != bundleAsset.Length)
        {
            return InvalidRemote(
                release,
                hasUploadedMarker,
                "downloaded asset length differs from escrow metadata");
        }

        ValidatedWave wave;
        try
        {
            wave = await LoadPreparedFilesAsync(
                markerPath,
                versionCommit,
                Path.Combine(operationDirectory, "extracted"),
                cancellationToken);
        }
        catch (InvalidOperationException exception)
        {
            return InvalidRemote(release, hasUploadedMarker, exception.Message, exception);
        }

        if (byName.TryGetValue(ReleaseWaveCompletion.FileName, out var completionAsset))
        {
            if (completionAsset.Length <= 0 ||
                completionAsset.Length >= PackagingConstants.ReleaseWave.MaximumMetadataEntryLength)
            {
                return InvalidRemote(
                    release,
                    hasUploadedMarker,
                    "completion receipt has an invalid length");
            }

            var downloadedCompletion = Path.Combine(filesDirectory, $"remote-{ReleaseWaveCompletion.FileName}");
            await escrow.DownloadAssetAsync(
                release.Id,
                ReleaseWaveCompletion.FileName,
                downloadedCompletion,
                cancellationToken);
            var downloadedBytes = await File.ReadAllBytesAsync(downloadedCompletion, cancellationToken);
            var expectedBytes = await File.ReadAllBytesAsync(wave.CompletionPath, cancellationToken);
            if (downloadedBytes.LongLength != completionAsset.Length ||
                !downloadedBytes.AsSpan().SequenceEqual(expectedBytes))
            {
                return InvalidRemote(
                    release,
                    hasUploadedMarker,
                    "completion receipt does not exactly match the prepared marker, bundle, and package hashes");
            }

            wave = wave with { HasCompletion = true };
        }

        return new RemoteWaveEvaluation(wave, null);
    }

    private async Task<ValidatedWave> LoadLocalAsync(
        string markerPath,
        string operationDirectory,
        CancellationToken cancellationToken)
    {
        markerPath = Path.GetFullPath(
            string.IsNullOrWhiteSpace(markerPath)
                ? throw new ArgumentException("A prepared marker path is required.", nameof(markerPath))
                : markerPath);
        var initial = await ReleaseWaveBundle.LoadAndValidateAsync(markerPath, cancellationToken);

        var snapshotDirectory = Path.Combine(operationDirectory, "local");
        Directory.CreateDirectory(snapshotDirectory);
        var snapshotMarker = Path.Combine(snapshotDirectory, PackagingConstants.ReleaseWave.MarkerFileName);
        var snapshotBundle = Path.Combine(snapshotDirectory, initial.Bundle.FileName);
        await CopyFileAsync(markerPath, snapshotMarker, cancellationToken);
        await CopyFileAsync(
            Path.Combine(Path.GetDirectoryName(markerPath)!, initial.Bundle.FileName),
            snapshotBundle,
            cancellationToken);

        return await LoadPreparedFilesAsync(
            snapshotMarker,
            initial.VersionCommit,
            Path.Combine(operationDirectory, "local-extracted"),
            cancellationToken);
    }

    private static async Task<ValidatedWave> LoadPreparedFilesAsync(
        string markerPath,
        string expectedVersionCommit,
        string extractionDirectory,
        CancellationToken cancellationToken)
    {
        var marker = await ReleaseWaveBundle.LoadAndValidateAsync(markerPath, cancellationToken);
        RequireWaveIdentity(marker, expectedVersionCommit);
        await ReleaseWaveBundle.ExtractAsync(
            markerPath,
            extractionDirectory,
            cancellationToken);
        var manifest = await PackagePipeline.LoadManifestAsync(
            Path.Combine(extractionDirectory, PackagingConstants.ManifestFileName),
            cancellationToken);
        if (manifest.Packages.Count == 0)
        {
            throw new InvalidOperationException(
                "An empty release wave has no escrow, tag, or Release; at least one package identity is required.");
        }

        var markerLength = new FileInfo(markerPath).Length;
        var markerHash = await HashFileAsync(markerPath, cancellationToken);
        var completionPath = Path.Combine(
            Path.GetDirectoryName(markerPath)!,
            ReleaseWaveCompletion.FileName);
        var completion = BuildCompletion(
            marker,
            manifest,
            extractionDirectory,
            markerLength,
            markerHash);
        var completionJson = JsonSerializer.Serialize(completion, JsonOptions) + "\n";
        await File.WriteAllTextAsync(
            completionPath,
            completionJson,
            Utf8NoBom,
            cancellationToken);
        if (new FileInfo(completionPath).Length >= PackagingConstants.ReleaseWave.MaximumMetadataEntryLength)
        {
            throw new InvalidOperationException("Release completion receipt exceeds the supported metadata bound.");
        }

        return new ValidatedWave(
            marker,
            manifest,
            markerPath,
            Path.Combine(Path.GetDirectoryName(markerPath)!, marker.Bundle.FileName),
            extractionDirectory,
            markerLength,
            markerHash,
            completionPath,
            HasCompletion: false);
    }

    private static ReleaseWaveCompletion BuildCompletion(
        ReleaseWave marker,
        ReleaseManifest manifest,
        string extractionDirectory,
        long markerLength,
        string markerHash) =>
        new()
        {
            Status = ReleaseWaveCompletion.CompleteStatus,
            PreviousVersionCommit = marker.PreviousVersionCommit,
            SourceCommit = marker.SourceCommit,
            VersionCommit = marker.VersionCommit,
            TagName = marker.TagName,
            TagCommit = marker.TagCommit,
            PreparedMarker = new ReleaseWaveFile(
                PackagingConstants.ReleaseWave.MarkerFileName,
                markerLength,
                markerHash),
            Bundle = marker.Bundle,
            Lineage = marker.Lineage,
            Manifest = marker.Manifest,
            PackageCount = manifest.Packages.Count,
            Packages = manifest.Packages.Select(package =>
            {
                var packageFile = package.PackageFile
                    ?? throw new InvalidOperationException(
                        $"Prepared package {package.Identity} has no nupkg filename.");
                var packageHash = package.PackageSha256
                    ?? throw new InvalidOperationException(
                        $"Prepared package {package.Identity} has no nupkg hash.");
                ReleaseWaveFile? symbols = null;
                if (package.SymbolsFile is not null)
                {
                    var symbolsHash = package.SymbolsSha256
                        ?? throw new InvalidOperationException(
                            $"Prepared package {package.Identity} has no symbols hash.");
                    symbols = new ReleaseWaveFile(
                        package.SymbolsFile,
                        new FileInfo(Path.Combine(extractionDirectory, package.SymbolsFile)).Length,
                        symbolsHash);
                }

                return new ReleaseWavePackageCompletion
                {
                    PackageId = package.PackageId,
                    Version = package.Version,
                    Package = new ReleaseWaveFile(
                        packageFile,
                        new FileInfo(Path.Combine(extractionDirectory, packageFile)).Length,
                        packageHash),
                    PackageDisposition = ReleaseWaveCompletion.AvailableDisposition,
                    Symbols = symbols,
                    SymbolsDisposition = symbols is null
                        ? ReleaseWaveCompletion.SymbolsNotRequiredDisposition
                        : ReleaseWaveCompletion.SymbolsAcceptedDisposition
                };
            }).ToList()
        };

    private async Task<bool> AnyPackageIsPublicAsync(
        ReleaseManifest manifest,
        CancellationToken cancellationToken)
    {
        foreach (var package in manifest.Packages)
        {
            if (await promotionTarget.ExistsAsync(
                    package.PackageId,
                    package.Version,
                    cancellationToken))
            {
                return true;
            }
        }
        return false;
    }

    private async Task<ReleaseWaveEscrowRelease?> FindSingleReleaseAsync(
        string tagName,
        CancellationToken cancellationToken)
    {
        var releases = await escrow.FindByTagIncludingDraftsAsync(tagName, cancellationToken);
        if (releases.Count > 1)
        {
            throw new InvalidOperationException(
                $"Release tag {tagName} resolves to {releases.Count} draft/published Releases; expected at most one.");
        }
        return releases.Count == 0 ? null : releases[0];
    }

    private async Task RequireTagAbsentOrExactAsync(
        string tagName,
        string versionCommit,
        CancellationToken cancellationToken)
    {
        var target = await escrow.ResolveTagTargetAsync(tagName, cancellationToken);
        if (target is null) return;
        target = RequireCommit(target, $"resolved target for {tagName}");
        if (!string.Equals(target, versionCommit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Release tag {tagName} targets {target}; expected {versionCommit}. Tags are never moved or forced.");
        }
    }

    private async Task RequirePublishedTerminalAsync(
        ReleaseWaveEscrowRelease release,
        ValidatedWave wave,
        string tagName,
        string versionCommit,
        CancellationToken cancellationToken)
    {
        if (release.IsDraft)
        {
            throw new InvalidOperationException($"Release {tagName} is still a draft.");
        }
        if (!release.IsImmutable)
        {
            throw new InvalidOperationException(
                $"Published Release {tagName} is mutable and cannot be durable exact escrow.");
        }
        if (!wave.HasCompletion)
        {
            throw new InvalidOperationException(
                $"Published Release {tagName} has no exact completion receipt.");
        }


        // The immutable receipt records that every nupkg passed WaitUntilAvailableAsync and every
        // symbols artifact was accepted-or-duplicate. Later registry outage or unlisting is
        // operational state; it cannot reinterpret completed historical custody.
        var target = await escrow.ResolveTagTargetAsync(tagName, cancellationToken);
        if (!string.Equals(target, versionCommit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Published Release {tagName} does not resolve to exact version commit {versionCommit}.");
        }
    }

    private static void RequireReleaseIdentity(
        ReleaseWaveEscrowRelease release,
        string tagName,
        string versionCommit)
    {
        if (string.IsNullOrWhiteSpace(release.Id))
        {
            throw new InvalidOperationException($"Release {tagName} has no stable escrow identifier.");
        }
        if (!string.Equals(release.TagName, tagName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Release discovery returned tag '{release.TagName}', expected '{tagName}'.");
        }
        var target = RequireCommit(release.TargetCommit, $"draft target for {tagName}");
        if (!string.Equals(target, versionCommit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Release {tagName} targets {target}; expected exact version commit {versionCommit}.");
        }
    }

    private static void RequireWaveIdentity(ReleaseWave marker, string versionCommit)
    {
        if (!string.Equals(marker.VersionCommit, versionCommit, StringComparison.Ordinal) ||
            !string.Equals(marker.TagCommit, versionCommit, StringComparison.Ordinal) ||
            !string.Equals(marker.TagName, TagName(versionCommit), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Prepared marker does not belong to exact release identity {TagName(versionCommit)}.");
        }
    }

    private static void RequireSamePreparedBytes(ValidatedWave local, ValidatedWave remote)
    {
        if (!string.Equals(local.MarkerHash, remote.MarkerHash, StringComparison.Ordinal) ||
            local.MarkerLength != remote.MarkerLength ||
            !string.Equals(local.Marker.Bundle.Sha256, remote.Marker.Bundle.Sha256, StringComparison.Ordinal) ||
            local.Marker.Bundle.Length != remote.Marker.Bundle.Length)
        {
            throw new InvalidOperationException(
                $"Release identity {local.Marker.TagName} already has different prepared bytes. " +
                "Exact escrow is immutable and cannot be replaced or rebuilt.");
        }
    }

    private static RemoteWaveEvaluation InvalidRemote(
        ReleaseWaveEscrowRelease release,
        bool hasUploadedMarker,
        string problem,
        Exception? inner = null)
    {
        if (!release.IsDraft || hasUploadedMarker)
        {
            var authority = release.IsDraft
                ? $"Draft Release {release.TagName} has an uploaded prepared marker"
                : $"Published Release {release.TagName}";
            throw new InvalidOperationException(
                $"{authority} but is not valid exact escrow: {problem}.",
                inner);
        }
        return new RemoteWaveEvaluation(null, problem);
    }

    private string CreateOperationDirectory(string operation)
    {
        Directory.CreateDirectory(scratchRoot);
        var path = Path.Combine(scratchRoot, $"release-wave-{operation}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteOperationDirectory(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static async Task<string> HashFileAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken))
            .ToLowerInvariant();
    }

    private static string RequireCommit(string value, string description)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length != 40 ||
            value.Any(character =>
                !(character is >= '0' and <= '9') &&
                !(character is >= 'a' and <= 'f')))
        {
            throw new InvalidOperationException(
                $"{description} must be a full lowercase 40-character Git commit.");
        }
        return value;
    }

    private static string TagName(string versionCommit) =>
        $"{PackagingConstants.ReleaseWave.TagPrefix}{versionCommit}";

    private static string BundleName(string versionCommit) =>
        $"{PackagingConstants.ReleaseWave.BundleFilePrefix}{versionCommit}{PackagingConstants.ReleaseWave.BundleFileExtension}";

    private sealed record RemoteWaveEvaluation(ValidatedWave? Wave, string? Problem);

    private sealed record ValidatedWave(
        ReleaseWave Marker,
        ReleaseManifest Manifest,
        string MarkerPath,
        string BundlePath,
        string ExtractionDirectory,
        long MarkerLength,
        string MarkerHash,
        string CompletionPath,
        bool HasCompletion);
}
