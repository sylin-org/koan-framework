using System.Security.Cryptography;
using System.Text.Json;
using Koan.Packaging.Models;
using Koan.Packaging.Services;

namespace Koan.Packaging.Infrastructure;

internal sealed class GitHubReleaseWaveEscrow : IReleaseWaveEscrow
{
    private readonly string repository;
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task<ProcessResult>> run;

    public GitHubReleaseWaveEscrow(
        string repositoryRoot,
        string repository,
        ProcessRunner processRunner)
        : this(
            repositoryRoot,
            repository,
            (arguments, cancellationToken) => processRunner.RunAsync(
                Cli.Executable,
                arguments,
                repositoryRoot,
                cancellationToken))
    {
        ArgumentNullException.ThrowIfNull(processRunner);
    }

    internal GitHubReleaseWaveEscrow(
        string repositoryRoot,
        string repository,
        Func<IReadOnlyList<string>, CancellationToken, Task<ProcessResult>> run)
    {
        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            throw new ArgumentException("Repository root is required.", nameof(repositoryRoot));
        }

        var fullRoot = Path.GetFullPath(repositoryRoot);
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException($"GitHub escrow repository root does not exist: {fullRoot}");
        }

        this.repository = RequireRepository(repository);
        this.run = run ?? throw new ArgumentNullException(nameof(run));
    }

    public async Task<IReadOnlyList<ReleaseWaveEscrowRelease>> FindByTagIncludingDraftsAsync(
        string tagName,
        CancellationToken cancellationToken)
    {
        tagName = RequireTagName(tagName);
        var releases = await ListReleaseDetailsAsync(cancellationToken);
        return releases
            .Where(release => string.Equals(release.TagName, tagName, StringComparison.Ordinal))
            .Select(ToPortRelease)
            .ToArray();
    }

    public async Task<ReleaseWaveEscrowRelease> CreateDraftAsync(
        string tagName,
        string targetCommit,
        CancellationToken cancellationToken)
    {
        tagName = RequireTagName(tagName);
        targetCommit = RequireCommit(targetCommit, "draft target commit");

        var existing = await FindByTagIncludingDraftsAsync(tagName, cancellationToken);
        if (existing.Count != 0)
        {
            throw new InvalidOperationException(
                $"Cannot create GitHub release draft '{tagName}': {existing.Count} release record(s) already use that exact tag.");
        }

        var existingTagTarget = await ResolveTagTargetAsync(tagName, cancellationToken);
        RequireCompatibleTagTarget(tagName, targetCommit, existingTagTarget);

        var result = await RunAsync(
            [
                "release", "create", tagName,
                "--repo", repository,
                "--draft",
                "--latest=false",
                "--target", targetCommit,
                "--title", tagName,
                "--notes", Cli.DraftNotes
            ],
            cancellationToken);

        IReadOnlyList<ReleaseWaveEscrowRelease> reconciled;
        try
        {
            reconciled = await FindByTagIncludingDraftsAsync(tagName, cancellationToken);
        }
        catch (Exception reconciliationError) when (result.ExitCode != 0)
        {
            throw ReconciliationFailure("create GitHub release draft", result, reconciliationError);
        }

        if (reconciled.Count == 1)
        {
            var release = reconciled[0];
            if (!release.IsDraft)
            {
                throw new InvalidOperationException(
                    $"GitHub release '{tagName}' was expected to be a draft after creation, but it is already published.");
            }

            RequireSameCommit(release.TargetCommit, targetCommit, $"GitHub release '{tagName}' target");
            var tagTarget = await ResolveTagTargetAsync(tagName, cancellationToken);
            RequireCompatibleTagTarget(tagName, targetCommit, tagTarget);
            return release;
        }

        if (reconciled.Count > 1)
        {
            throw new InvalidOperationException(
                $"GitHub returned {reconciled.Count} release records for exact tag '{tagName}'; release identity is ambiguous.");
        }

        if (result.ExitCode != 0)
        {
            throw CommandFailure("create GitHub release draft", result);
        }

        throw new InvalidOperationException(
            $"GitHub reported successful draft creation for '{tagName}', but the exact draft cannot be found.");
    }

    public async Task DeleteDraftAsync(string releaseId, CancellationToken cancellationToken)
    {
        releaseId = RequireReleaseId(releaseId);
        var release = await RequireReleaseByIdAsync(releaseId, cancellationToken);
        if (!release.IsDraft)
        {
            throw new InvalidOperationException(
                $"GitHub release '{release.TagName}' ({release.Id}) is published and cannot be reset as an incomplete draft.");
        }

        var assets = await ListAssetDetailsAsync(releaseId, cancellationToken);
        if (assets.Any(asset => asset.IsUploaded && string.Equals(
                asset.Name,
                PackagingConstants.ReleaseWave.MarkerFileName,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"GitHub release draft '{release.TagName}' contains prepared marker " +
                $"'{PackagingConstants.ReleaseWave.MarkerFileName}' and cannot be reset.");
        }

        var result = await RunAsync(
            ApiArguments(
                $"repos/{repository}/releases/{releaseId}",
                "--method", "DELETE",
                "--silent"),
            cancellationToken);

        IReadOnlyList<ReleaseDetail> remaining;
        try
        {
            remaining = await ListReleaseDetailsAsync(cancellationToken);
        }
        catch (Exception reconciliationError) when (result.ExitCode != 0)
        {
            throw ReconciliationFailure("delete incomplete GitHub release draft", result, reconciliationError);
        }

        if (remaining.All(candidate => !string.Equals(candidate.Id, releaseId, StringComparison.Ordinal)))
        {
            return;
        }

        if (result.ExitCode != 0)
        {
            throw CommandFailure("delete incomplete GitHub release draft", result);
        }

        throw new InvalidOperationException(
            $"GitHub reported successful deletion for release '{releaseId}', but the draft still exists.");
    }

    public async Task<IReadOnlyList<ReleaseWaveEscrowAsset>> ListAssetsAsync(
        string releaseId,
        CancellationToken cancellationToken)
    {
        releaseId = RequireReleaseId(releaseId);
        _ = await RequireReleaseByIdAsync(releaseId, cancellationToken);
        return (await ListAssetDetailsAsync(releaseId, cancellationToken))
            .Select(asset => new ReleaseWaveEscrowAsset(asset.Name, asset.Length, asset.IsUploaded))
            .ToArray();
    }

    public async Task DownloadAssetAsync(
        string releaseId,
        string assetName,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        releaseId = RequireReleaseId(releaseId);
        assetName = RequireAssetName(assetName);
        var release = await RequireReleaseByIdAsync(releaseId, cancellationToken);
        var asset = RequireExactAsset(
            await ListAssetDetailsAsync(releaseId, cancellationToken),
            assetName,
            release.TagName);

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("Asset destination path is required.", nameof(destinationPath));
        }

        var destination = Path.GetFullPath(destinationPath);
        if (File.Exists(destination))
        {
            throw new InvalidOperationException(
                $"Refusing to overwrite existing escrow download destination '{destination}'.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        ProcessResult result;
        try
        {
            result = await RunAsync(
                [
                    "release", "download", release.TagName,
                    "--repo", repository,
                    "--pattern", assetName,
                    "--output", destination
                ],
                cancellationToken);
        }
        catch
        {
            TryDelete(destination);
            throw;
        }

        try
        {
            await RequireDownloadedAssetAsync(asset, destination, cancellationToken);
        }
        catch (Exception reconciliationError)
        {
            TryDelete(destination);
            if (result.ExitCode != 0)
            {
                throw ReconciliationFailure(
                    $"download GitHub release asset '{assetName}'",
                    result,
                    reconciliationError);
            }
            throw;
        }

        if (result.ExitCode != 0)
        {
            // A completed exact download is authoritative even if the CLI lost its final response.
            return;
        }
    }

    public async Task UploadAssetAsync(
        string releaseId,
        string assetName,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        releaseId = RequireReleaseId(releaseId);
        assetName = RequireAssetName(assetName);
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Asset source path is required.", nameof(sourcePath));
        }

        var source = Path.GetFullPath(sourcePath);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException($"Escrow upload source does not exist: {source}", source);
        }
        if (source.Contains('#', StringComparison.Ordinal) ||
            !string.Equals(Path.GetFileName(source), assetName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Escrow upload source filename must exactly equal asset name '{assetName}' and must not contain '#': {source}");
        }

        var release = await RequireReleaseByIdAsync(releaseId, cancellationToken);
        if (!release.IsDraft)
        {
            throw new InvalidOperationException(
                $"Cannot upload '{assetName}' to published GitHub release '{release.TagName}'.");
        }

        var expected = new LocalAsset(
            new FileInfo(source).Length,
            await HashFileAsync(source, cancellationToken));
        var before = await ListAssetDetailsAsync(releaseId, cancellationToken);
        if (TryFindExactAsset(before, assetName, release.TagName, out var existing))
        {
            if (!existing.IsUploaded &&
                string.Equals(assetName, ReleaseWaveCompletion.FileName, StringComparison.Ordinal))
            {
                var deleteResult = await RunAsync(
                    ApiArguments(
                        $"repos/{repository}/releases/assets/{existing.Id}",
                        "--method", "DELETE",
                        "--silent"),
                    cancellationToken);
                var afterDelete = await ListAssetDetailsAsync(releaseId, cancellationToken);
                if (TryFindExactAsset(afterDelete, assetName, release.TagName, out var reconciled))
                {
                    if (reconciled.IsUploaded)
                    {
                        // GitHub may have completed the exact upload while deletion was in flight.
                        RequireSameAsset(reconciled, expected, release.TagName);
                        return;
                    }

                    if (deleteResult.ExitCode != 0)
                    {
                        throw CommandFailure(
                            $"delete incomplete GitHub release asset '{assetName}'",
                            deleteResult);
                    }

                    throw new InvalidOperationException(
                        $"GitHub reported deletion of incomplete completion asset '{assetName}', " +
                        $"but it remains in starter state on release '{release.TagName}'.");
                }

                // Absence is authoritative even if the delete response was lost. Only the
                // deterministic completion receipt is recoverable this way; prepared assets are not.
            }
            else
            {
                RequireSameAsset(existing, expected, release.TagName);
                return;
            }
        }

        var result = await RunAsync(
            ["release", "upload", release.TagName, source, "--repo", repository],
            cancellationToken);

        IReadOnlyList<AssetDetail> after;
        try
        {
            after = await ListAssetDetailsAsync(releaseId, cancellationToken);
        }
        catch (Exception reconciliationError) when (result.ExitCode != 0)
        {
            throw ReconciliationFailure($"upload GitHub release asset '{assetName}'", result, reconciliationError);
        }

        if (TryFindExactAsset(after, assetName, release.TagName, out var uploaded))
        {
            RequireSameAsset(uploaded, expected, release.TagName);
            return;
        }

        if (result.ExitCode != 0)
        {
            throw CommandFailure($"upload GitHub release asset '{assetName}'", result);
        }

        throw new InvalidOperationException(
            $"GitHub reported successful upload of '{assetName}', but the exact asset is absent from release '{release.TagName}'.");
    }

    public async Task PublishAsync(
        string releaseId,
        string tagName,
        string versionCommit,
        CancellationToken cancellationToken)
    {
        releaseId = RequireReleaseId(releaseId);
        tagName = RequireTagName(tagName);
        versionCommit = RequireCommit(versionCommit, "release version commit");

        var release = await RequireReleaseByIdAsync(releaseId, cancellationToken);
        RequireReleaseIdentity(release, tagName, versionCommit);

        var tagTarget = await ResolveTagTargetAsync(tagName, cancellationToken);
        if (tagTarget is null)
        {
            if (!release.IsDraft)
            {
                throw new InvalidOperationException(
                    $"Published GitHub release '{tagName}' has no exact remote tag ref; publication evidence is invalid.");
            }

            var createTagResult = await RunAsync(
                ApiArguments(
                    $"repos/{repository}/git/refs",
                    "--method", "POST",
                    "--raw-field", $"ref=refs/tags/{tagName}",
                    "--raw-field", $"sha={versionCommit}",
                    "--silent"),
                cancellationToken);

            string? reconciledTarget;
            try
            {
                reconciledTarget = await ResolveTagTargetAsync(tagName, cancellationToken);
            }
            catch (Exception reconciliationError) when (createTagResult.ExitCode != 0)
            {
                throw ReconciliationFailure($"create exact GitHub tag '{tagName}'", createTagResult, reconciliationError);
            }

            if (reconciledTarget is null)
            {
                if (createTagResult.ExitCode != 0)
                {
                    throw CommandFailure($"create exact GitHub tag '{tagName}'", createTagResult);
                }

                throw new InvalidOperationException(
                    $"GitHub reported successful creation of tag '{tagName}', but the tag ref is absent.");
            }

            RequireSameCommit(reconciledTarget, versionCommit, $"GitHub tag '{tagName}'");
        }
        else
        {
            RequireSameCommit(tagTarget, versionCommit, $"GitHub tag '{tagName}'");
        }

        if (!release.IsDraft)
        {
            if (!release.IsImmutable)
            {
                throw new InvalidOperationException(
                    $"Published GitHub release '{tagName}' is not immutable; terminal release evidence fails closed.");
            }
            return;
        }

        var publishResult = await RunAsync(
            ApiArguments(
                $"repos/{repository}/releases/{releaseId}",
                "--method", "PATCH",
                "--field", "draft=false",
                "--raw-field", "make_latest=false",
                "--silent"),
            cancellationToken);

        ReleaseWaveEscrowRelease published;
        try
        {
            published = await RequireReleaseByIdAsync(releaseId, cancellationToken);
        }
        catch (Exception reconciliationError) when (publishResult.ExitCode != 0)
        {
            throw ReconciliationFailure($"publish GitHub release '{tagName}'", publishResult, reconciliationError);
        }

        RequireReleaseIdentity(published, tagName, versionCommit);
        if (published.IsDraft)
        {
            if (publishResult.ExitCode != 0)
            {
                throw CommandFailure($"publish GitHub release '{tagName}'", publishResult);
            }

            throw new InvalidOperationException(
                $"GitHub reported successful publication of '{tagName}', but the release remains a draft.");
        }
        if (!published.IsImmutable)
        {
            throw new InvalidOperationException(
                $"GitHub release '{tagName}' was published without immutable-release protection; terminal evidence fails closed.");
        }

        var publishedTagTarget = await ResolveTagTargetAsync(tagName, cancellationToken);
        RequireSameCommit(
            publishedTagTarget ?? throw new InvalidOperationException(
                $"Published GitHub release '{tagName}' has no exact remote tag ref."),
            versionCommit,
            $"published GitHub tag '{tagName}'");
    }

    public async Task<string?> ResolveTagTargetAsync(
        string tagName,
        CancellationToken cancellationToken)
    {
        tagName = RequireTagName(tagName);
        var output = await RequireAsync(
            ApiArguments($"repos/{repository}/git/matching-refs/tags/{tagName}"),
            $"resolve GitHub tag '{tagName}'",
            cancellationToken);
        using var document = ParseJson(output, $"GitHub matching refs for '{tagName}'");
        var root = RequireArray(document.RootElement, $"GitHub matching refs for '{tagName}'");
        var qualifiedName = $"refs/tags/{tagName}";
        var matches = new List<GitObject>();
        foreach (var element in root.EnumerateArray())
        {
            var item = RequireObject(element, "GitHub matching-ref item");
            var reference = RequireString(item, "ref", "GitHub matching-ref item");
            var target = ParseGitObject(
                RequireProperty(item, "object", "GitHub matching-ref item"),
                $"GitHub ref '{reference}' target");
            if (string.Equals(reference, qualifiedName, StringComparison.Ordinal))
            {
                matches.Add(target);
            }
        }

        if (matches.Count == 0) return null;
        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                $"GitHub returned {matches.Count} exact refs for '{qualifiedName}'; tag identity is ambiguous.");
        }

        return await PeelToCommitAsync(matches[0], tagName, cancellationToken);
    }

    private async Task<IReadOnlyList<ReleaseDetail>> ListReleaseDetailsAsync(CancellationToken cancellationToken)
    {
        var output = await RequireAsync(
            ApiArguments(
                $"repos/{repository}/releases?per_page={Cli.PageSize}",
                "--paginate",
                "--slurp"),
            "list GitHub releases including drafts",
            cancellationToken);
        using var document = ParseJson(output, "GitHub release list");
        var pages = RequireArray(document.RootElement, "GitHub release page list");
        var releases = new List<ReleaseDetail>();
        foreach (var pageElement in pages.EnumerateArray())
        {
            var page = RequireArray(pageElement, "GitHub release page");
            foreach (var releaseElement in page.EnumerateArray())
            {
                releases.Add(ParseRelease(releaseElement));
            }
        }

        var duplicateIds = releases
            .GroupBy(release => release.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateIds is not null)
        {
            throw new InvalidOperationException(
                $"GitHub release JSON contains duplicate release id '{duplicateIds.Key}'.");
        }

        return releases;
    }

    private async Task<ReleaseWaveEscrowRelease> RequireReleaseByIdAsync(
        string releaseId,
        CancellationToken cancellationToken)
    {
        var matches = (await ListReleaseDetailsAsync(cancellationToken))
            .Where(release => string.Equals(release.Id, releaseId, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length == 0)
        {
            throw new InvalidOperationException($"GitHub release id '{releaseId}' does not exist.");
        }
        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"GitHub returned {matches.Length} records for release id '{releaseId}'; release identity is ambiguous.");
        }

        return ToPortRelease(matches[0]);
    }

    private async Task<IReadOnlyList<AssetDetail>> ListAssetDetailsAsync(
        string releaseId,
        CancellationToken cancellationToken)
    {
        var output = await RequireAsync(
            ApiArguments(
                $"repos/{repository}/releases/{releaseId}/assets?per_page={Cli.PageSize}",
                "--paginate",
                "--slurp"),
            $"list assets for GitHub release '{releaseId}'",
            cancellationToken);
        using var document = ParseJson(output, $"GitHub release '{releaseId}' asset list");
        var pages = RequireArray(document.RootElement, "GitHub release asset page list");
        var assets = new List<AssetDetail>();
        foreach (var pageElement in pages.EnumerateArray())
        {
            var page = RequireArray(pageElement, "GitHub release asset page");
            foreach (var assetElement in page.EnumerateArray())
            {
                assets.Add(ParseAsset(assetElement));
            }
        }

        var duplicateId = assets
            .GroupBy(asset => asset.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
        {
            throw new InvalidOperationException(
                $"GitHub asset JSON contains duplicate asset id '{duplicateId.Key}'.");
        }

        var duplicateName = assets
            .GroupBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateName is not null)
        {
            throw new InvalidOperationException(
                $"GitHub release '{releaseId}' contains duplicate or case-aliased asset name '{duplicateName.Key}'.");
        }

        return assets.OrderBy(asset => asset.Name, StringComparer.Ordinal).ToArray();
    }

    private async Task<string> PeelToCommitAsync(
        GitObject target,
        string tagName,
        CancellationToken cancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var depth = 0; depth < Cli.MaximumTagDepth; depth++)
        {
            if (string.Equals(target.Type, "commit", StringComparison.Ordinal)) return target.Sha;
            if (!string.Equals(target.Type, "tag", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"GitHub tag '{tagName}' resolves to unsupported Git object type '{target.Type}', not a commit.");
            }
            if (!visited.Add(target.Sha))
            {
                throw new InvalidOperationException(
                    $"GitHub tag '{tagName}' contains a cyclic annotated-tag chain at '{target.Sha}'.");
            }

            var output = await RequireAsync(
                ApiArguments($"repos/{repository}/git/tags/{target.Sha}"),
                $"peel annotated GitHub tag '{tagName}'",
                cancellationToken);
            using var document = ParseJson(output, $"GitHub annotated tag object '{target.Sha}'");
            var root = RequireObject(document.RootElement, $"GitHub annotated tag object '{target.Sha}'");
            target = ParseGitObject(
                RequireProperty(root, "object", $"GitHub annotated tag object '{target.Sha}'"),
                $"GitHub annotated tag '{target.Sha}' target");
        }

        throw new InvalidOperationException(
            $"GitHub tag '{tagName}' exceeds the supported annotated-tag peel depth of {Cli.MaximumTagDepth}.");
    }

    private async Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken) =>
        await run(arguments, cancellationToken);

    private async Task<string> RequireAsync(
        IReadOnlyList<string> arguments,
        string operation,
        CancellationToken cancellationToken)
    {
        var result = await RunAsync(arguments, cancellationToken);
        if (result.ExitCode != 0) throw CommandFailure(operation, result);
        return result.StandardOutput.Trim();
    }

    private static IReadOnlyList<string> ApiArguments(string endpoint, params string[] arguments)
    {
        var result = new List<string>
        {
            "api",
            "--header", Cli.AcceptHeader,
            "--header", Cli.ApiVersionHeader,
            endpoint
        };
        result.AddRange(arguments);
        return result;
    }

    private static ReleaseDetail ParseRelease(JsonElement element)
    {
        var release = RequireObject(element, "GitHub release");
        var id = RequirePositiveId(release, "id", "GitHub release");
        var tagName = RequireString(release, "tag_name", $"GitHub release '{id}'");
        var targetCommit = RequireString(release, "target_commitish", $"GitHub release '{id}'");
        var isDraft = RequireBoolean(release, "draft", $"GitHub release '{id}'");
        var isImmutable = RequireBoolean(release, "immutable", $"GitHub release '{id}'");
        var isPrerelease = RequireBoolean(release, "prerelease", $"GitHub release '{id}'");
        return new ReleaseDetail(id, tagName, targetCommit, isDraft, isImmutable, isPrerelease);
    }

    private static ReleaseWaveEscrowRelease ToPortRelease(ReleaseDetail release)
    {
        var targetCommit = RequireCommit(release.TargetCommit, $"GitHub release '{release.TagName}' target");
        _ = RequireTagName(release.TagName);
        if (release.IsDraft && release.IsImmutable)
        {
            throw new InvalidOperationException(
                $"GitHub release '{release.TagName}' is both draft and immutable; the release state is invalid.");
        }
        if (release.IsPrerelease)
        {
            throw new InvalidOperationException(
                $"GitHub release '{release.TagName}' is a prerelease; release-wave escrow requires an ordinary release.");
        }

        return new ReleaseWaveEscrowRelease(
            release.Id,
            release.TagName,
            targetCommit,
            release.IsDraft,
            release.IsImmutable);
    }

    private static AssetDetail ParseAsset(JsonElement element)
    {
        var asset = RequireObject(element, "GitHub release asset");
        var id = RequirePositiveId(asset, "id", "GitHub release asset");
        var name = RequireAssetName(RequireString(asset, "name", $"GitHub release asset '{id}'"));
        var length = RequireNonNegativeInt64(asset, "size", $"GitHub release asset '{id}'");
        var state = RequireString(asset, "state", $"GitHub release asset '{id}'");
        var isUploaded = state switch
        {
            "uploaded" => true,
            "starter" => false,
            _ => throw new InvalidOperationException(
                $"GitHub release asset '{name}' ({id}) is in unsupported state '{state}'; expected 'uploaded' or recoverable 'starter'.")
        };
        string? sha256 = null;
        if (asset.TryGetProperty("digest", out var digestElement) && digestElement.ValueKind != JsonValueKind.Null)
        {
            if (digestElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException(
                    $"GitHub release asset '{name}' ({id}) has a non-string digest.");
            }
            sha256 = ParseDigest(digestElement.GetString(), name);
        }

        return new AssetDetail(id, name, length, sha256, isUploaded);
    }

    private static GitObject ParseGitObject(JsonElement element, string description)
    {
        var value = RequireObject(element, description);
        var sha = RequireCommit(RequireString(value, "sha", description), $"{description} SHA");
        var type = RequireString(value, "type", description);
        return new GitObject(sha, type);
    }

    private static AssetDetail RequireExactAsset(
        IReadOnlyList<AssetDetail> assets,
        string assetName,
        string tagName)
    {
        if (!TryFindExactAsset(assets, assetName, tagName, out var asset))
        {
            throw new InvalidOperationException(
                $"GitHub release '{tagName}' does not contain exact asset '{assetName}'.");
        }
        if (!asset.IsUploaded)
        {
            throw new InvalidOperationException(
                $"GitHub release '{tagName}' asset '{assetName}' is an incomplete 'starter', not downloadable escrow. " +
                "Reset and restage the incomplete draft.");
        }
        return asset;
    }

    private static bool TryFindExactAsset(
        IReadOnlyList<AssetDetail> assets,
        string assetName,
        string tagName,
        out AssetDetail asset)
    {
        var alias = assets.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, assetName, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(candidate.Name, assetName, StringComparison.Ordinal));
        if (alias is not null)
        {
            throw new InvalidOperationException(
                $"GitHub release '{tagName}' contains case-aliased asset '{alias.Name}' instead of exact '{assetName}'.");
        }

        asset = assets.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, assetName, StringComparison.Ordinal))!;
        return asset is not null;
    }

    private static async Task RequireDownloadedAssetAsync(
        AssetDetail expected,
        string destination,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(destination))
        {
            throw new InvalidOperationException(
                $"GitHub reported asset download success, but destination '{destination}' was not created.");
        }

        var actualLength = new FileInfo(destination).Length;
        if (actualLength != expected.Length)
        {
            throw new InvalidOperationException(
                $"Downloaded GitHub asset '{expected.Name}' length mismatch: metadata records {expected.Length}, file has {actualLength}.");
        }
        if (expected.Sha256 is not null)
        {
            var actualHash = await HashFileAsync(destination, cancellationToken);
            if (!string.Equals(actualHash, expected.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Downloaded GitHub asset '{expected.Name}' digest mismatch: metadata records {expected.Sha256}, file has {actualHash}.");
            }
        }
    }

    private static void RequireSameAsset(AssetDetail actual, LocalAsset expected, string tagName)
    {
        if (!actual.IsUploaded)
        {
            throw new InvalidOperationException(
                $"GitHub release '{tagName}' contains incomplete starter asset '{actual.Name}'. " +
                "It cannot prove exact bytes; reset and restage the incomplete draft.");
        }
        if (actual.Length != expected.Length)
        {
            throw new InvalidOperationException(
                $"GitHub release '{tagName}' already contains '{actual.Name}' with length {actual.Length}; " +
                $"the verified source length is {expected.Length}. Refusing to clobber escrow bytes.");
        }
        if (actual.Sha256 is null)
        {
            throw new InvalidOperationException(
                $"GitHub release '{tagName}' already contains '{actual.Name}', but GitHub supplied no SHA-256 digest. " +
                "Exact upload reconciliation cannot be proved and will not overwrite the asset.");
        }
        if (!string.Equals(actual.Sha256, expected.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"GitHub release '{tagName}' already contains '{actual.Name}' with SHA-256 {actual.Sha256}; " +
                $"the verified source has {expected.Sha256}. Refusing to clobber escrow bytes.");
        }
    }

    private static void RequireReleaseIdentity(
        ReleaseWaveEscrowRelease release,
        string tagName,
        string versionCommit)
    {
        if (!string.Equals(release.TagName, tagName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"GitHub release id '{release.Id}' belongs to tag '{release.TagName}', not expected '{tagName}'.");
        }
        RequireSameCommit(release.TargetCommit, versionCommit, $"GitHub release '{tagName}' target");
    }

    private static void RequireCompatibleTagTarget(
        string tagName,
        string expectedCommit,
        string? actualCommit)
    {
        if (actualCommit is not null)
        {
            RequireSameCommit(actualCommit, expectedCommit, $"GitHub tag '{tagName}'");
        }
    }

    private static void RequireSameCommit(string actual, string expected, string description)
    {
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{description} resolves to {actual}, expected exact commit {expected}. Refusing to move or replace the tag.");
        }
    }

    private static string RequireRepository(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("GitHub repository must use owner/repo form.", nameof(value));
        }
        if (value.Length > Cli.MaximumRepositoryLength ||
            value.Count(character => character == '/') != 1 ||
            value.Any(character => !IsRepositoryCharacter(character)))
        {
            throw new ArgumentException(
                $"GitHub repository '{value}' is invalid; expected an ASCII owner/repo name.",
                nameof(value));
        }

        var parts = value.Split('/');
        if (parts.Any(part => part.Length == 0 || part is "." or "..") ||
            parts[0].StartsWith("-", StringComparison.Ordinal) ||
            parts[0].EndsWith("-", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"GitHub repository '{value}' is invalid; expected an ASCII owner/repo name.",
                nameof(value));
        }
        return value;
    }

    private static string RequireTagName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > Cli.MaximumTagLength ||
            value[0] is '.' or '/' or '-' ||
            value[^1] is '.' or '/' or '-' ||
            value.Contains("..", StringComparison.Ordinal) ||
            value.Contains("//", StringComparison.Ordinal) ||
            value.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) ||
            value.Any(character => !IsTagCharacter(character)))
        {
            throw new ArgumentException(
                $"GitHub release tag '{value}' is not a supported exact Git ref name.",
                nameof(value));
        }
        return value;
    }

    private static string RequireAssetName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > Cli.MaximumAssetNameLength ||
            value[0] == '.' ||
            value.Any(character => !IsAssetCharacter(character)))
        {
            throw new ArgumentException(
                $"GitHub release asset name '{value}' is invalid; release-wave assets use ASCII letters, digits, '.', '_' and '-'.",
                nameof(value));
        }
        return value;
    }

    private static string RequireReleaseId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(character => character is < '0' or > '9'))
        {
            throw new ArgumentException(
                $"GitHub release id '{value}' is invalid.",
                nameof(value));
        }
        return value;
    }

    private static string RequireCommit(string value, string description)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length != Cli.FullCommitLength ||
            value.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException(
                $"{description} must be a full {Cli.FullCommitLength}-character Git commit SHA; received '{value}'.");
        }
        return value.ToLowerInvariant();
    }

    private static string ParseDigest(string? value, string assetName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !value.StartsWith(Cli.Sha256Prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"GitHub release asset '{assetName}' has unsupported digest '{value ?? "<null>"}'; expected sha256:<64 hex>.");
        }

        var hash = value[Cli.Sha256Prefix.Length..];
        if (hash.Length != Cli.Sha256Length || hash.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException(
                $"GitHub release asset '{assetName}' has malformed SHA-256 digest '{value}'.");
        }
        return hash.ToLowerInvariant();
    }

    private static JsonDocument ParseJson(string value, string description)
    {
        try
        {
            return JsonDocument.Parse(value);
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException($"{description} returned malformed JSON: {error.Message}", error);
        }
    }

    private static JsonElement RequireObject(JsonElement value, string description)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException($"{description} must be a JSON object.");
        }
        return value;
    }

    private static JsonElement RequireArray(JsonElement value, string description)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"{description} must be a JSON array.");
        }
        return value;
    }

    private static JsonElement RequireProperty(JsonElement value, string name, string description)
    {
        if (!value.TryGetProperty(name, out var property))
        {
            throw new InvalidOperationException($"{description} is missing required property '{name}'.");
        }
        return property;
    }

    private static string RequireString(JsonElement value, string name, string description)
    {
        var property = RequireProperty(value, name, description);
        if (property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidOperationException($"{description} property '{name}' must be a non-empty string.");
        }
        return property.GetString()!;
    }

    private static bool RequireBoolean(JsonElement value, string name, string description)
    {
        var property = RequireProperty(value, name, description);
        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new InvalidOperationException($"{description} property '{name}' must be a boolean.");
        }
        return property.GetBoolean();
    }

    private static string RequirePositiveId(JsonElement value, string name, string description)
    {
        var property = RequireProperty(value, name, description);
        if (property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt64(out var id) ||
            id <= 0)
        {
            throw new InvalidOperationException($"{description} property '{name}' must be a positive integer.");
        }
        return id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static long RequireNonNegativeInt64(JsonElement value, string name, string description)
    {
        var property = RequireProperty(value, name, description);
        if (property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt64(out var result) ||
            result < 0)
        {
            throw new InvalidOperationException($"{description} property '{name}' must be a non-negative integer.");
        }
        return result;
    }

    private static InvalidOperationException CommandFailure(string operation, ProcessResult result)
    {
        var detail = string.Join(
            Environment.NewLine,
            new[] { result.StandardError.Trim(), result.StandardOutput.Trim() }
                .Where(value => value.Length > 0));
        return new InvalidOperationException(
            $"gh failed to {operation} with exit code {result.ExitCode}." +
            (detail.Length == 0 ? string.Empty : Environment.NewLine + detail));
    }

    private static InvalidOperationException ReconciliationFailure(
        string operation,
        ProcessResult result,
        Exception reconciliationError) =>
        new(
            $"gh failed to {operation} with exit code {result.ExitCode}, and exact-state reconciliation also failed: " +
            reconciliationError.Message,
            new AggregateException(CommandFailure(operation, result), reconciliationError));

    private static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private static bool IsRepositoryCharacter(char value) =>
        value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.' or '/';

    private static bool IsTagCharacter(char value) =>
        value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.' or '/';

    private static bool IsAssetCharacter(char value) =>
        value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_' or '.';

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Preserve the original integrity failure; the caller owns its destination directory.
        }
    }

    private sealed record ReleaseDetail(
        string Id,
        string TagName,
        string TargetCommit,
        bool IsDraft,
        bool IsImmutable,
        bool IsPrerelease);

    private sealed record AssetDetail(
        string Id,
        string Name,
        long Length,
        string? Sha256,
        bool IsUploaded);

    private sealed record GitObject(string Sha, string Type);

    private sealed record LocalAsset(long Length, string Sha256);

    private static class Cli
    {
        public const string Executable = "gh";
        public const string AcceptHeader = "Accept: application/vnd.github+json";
        public const string ApiVersionHeader = "X-GitHub-Api-Version: 2026-03-10";
        public const string DraftNotes = "Exact Koan release-wave escrow. Publication is completed by automation after registry convergence.";
        public const string Sha256Prefix = "sha256:";
        public const int Sha256Length = 64;
        public const int FullCommitLength = 40;
        public const int MaximumTagDepth = 16;
        public const int PageSize = 100;
        public const int MaximumRepositoryLength = 141;
        public const int MaximumTagLength = 200;
        public const int MaximumAssetNameLength = 255;
    }
}
