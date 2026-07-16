using System.Security.Cryptography;
using System.Text.Json;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class GitHubReleaseWaveEscrowTests
{
    private const string Repository = "sylin-org/koan-framework";
    private const string TagName = "release/dev/cccccccccccccccccccccccccccccccccccccccc";
    private const string VersionCommit = "cccccccccccccccccccccccccccccccccccccccc";
    private const string OtherCommit = "dddddddddddddddddddddddddddddddddddddddd";
    private const string TagObjectOne = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string TagObjectTwo = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public async Task ExactTagDiscoveryIncludesDraftAndPublishedImmutableRecords()
    {
        var script = new ScriptedGh();
        script.Enqueue(
            RequireReleaseList,
            Success(ReleasePages(
                Release(11, TagName, VersionCommit, draft: true, immutable: false),
                Release(12, "release/dev/unrelated", OtherCommit, draft: false, immutable: false),
                Release(13, TagName, VersionCommit, draft: false, immutable: true))));
        var escrow = CreateEscrow(script);

        var releases = await escrow.FindByTagIncludingDraftsAsync(TagName, CancellationToken.None);

        Assert.Collection(
            releases,
            draft =>
            {
                Assert.Equal("11", draft.Id);
                Assert.True(draft.IsDraft);
                Assert.False(draft.IsImmutable);
            },
            published =>
            {
                Assert.Equal("13", published.Id);
                Assert.False(published.IsDraft);
                Assert.True(published.IsImmutable);
            });
        script.AssertComplete();
    }

    [Fact]
    public async Task DiscoveryRejectsMalformedImmutableState()
    {
        var malformed = JsonSerializer.Serialize(new object[]
        {
            new object[]
            {
                new
                {
                    id = 11,
                    tag_name = TagName,
                    target_commitish = VersionCommit,
                    draft = false,
                    immutable = "yes",
                    prerelease = false
                }
            }
        });
        var script = new ScriptedGh();
        script.Enqueue(RequireReleaseList, Success(malformed));
        var escrow = CreateEscrow(script);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            escrow.FindByTagIncludingDraftsAsync(TagName, CancellationToken.None));

        Assert.Contains("immutable", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("boolean", error.Message, StringComparison.OrdinalIgnoreCase);
        script.AssertComplete();
    }

    [Fact]
    public async Task DiscoveryReturnsNonImmutablePublishedStateForCoordinatorRejection()
    {
        var script = new ScriptedGh();
        script.Enqueue(
            RequireReleaseList,
            Success(ReleasePages(Release(14, TagName, VersionCommit, draft: false, immutable: false))));
        var escrow = CreateEscrow(script);

        var releases = await escrow.FindByTagIncludingDraftsAsync(TagName, CancellationToken.None);

        var published = Assert.Single(releases);
        Assert.False(published.IsDraft);
        Assert.False(published.IsImmutable);
        script.AssertComplete();
    }

    [Fact]
    public async Task DraftCreationAllowsAPendingTagAndReconcilesTheExactDraft()
    {
        var script = new ScriptedGh();
        script.Enqueue(RequireReleaseList, Success(ReleasePages()));
        script.Enqueue(RequireMatchingRefs, Success(MatchingRefs()));
        script.Enqueue(
            arguments =>
            {
                AssertStartsWith(arguments, "release", "create", TagName);
                AssertOption(arguments, "--repo", Repository);
                AssertOption(arguments, "--target", VersionCommit);
                Assert.Contains("--draft", arguments);
            },
            Success("https://github.com/example/release"));
        script.Enqueue(
            RequireReleaseList,
            Success(ReleasePages(Release(21, TagName, VersionCommit, draft: true, immutable: false))));
        script.Enqueue(RequireMatchingRefs, Success(MatchingRefs()));
        var escrow = CreateEscrow(script);

        var release = await escrow.CreateDraftAsync(TagName, VersionCommit, CancellationToken.None);

        Assert.Equal("21", release.Id);
        Assert.True(release.IsDraft);
        script.AssertComplete();
    }

    [Fact]
    public async Task PreparedMarkerPreventsDraftReset()
    {
        var script = new ScriptedGh();
        script.Enqueue(
            RequireReleaseList,
            Success(ReleasePages(Release(31, TagName, VersionCommit, draft: true, immutable: false))));
        script.Enqueue(
            RequireAssetList,
            Success(AssetPages(Asset(
                301,
                PackagingConstants.ReleaseWave.MarkerFileName,
                "marker"u8.ToArray()))));
        var escrow = CreateEscrow(script);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            escrow.DeleteDraftAsync("31", CancellationToken.None));

        Assert.Contains("prepared marker", error.Message, StringComparison.OrdinalIgnoreCase);
        script.AssertComplete();
    }

    [Fact]
    public async Task StarterMarkerAllowsIncompleteDraftReset()
    {
        var starter = new
        {
            id = 302,
            name = PackagingConstants.ReleaseWave.MarkerFileName,
            size = 7,
            state = "starter",
            digest = (string?)null
        };
        var script = new ScriptedGh();
        script.Enqueue(
            RequireReleaseList,
            Success(ReleasePages(Release(33, TagName, VersionCommit, draft: true, immutable: false))));
        script.Enqueue(RequireAssetList, Success(AssetPages(starter)));
        script.Enqueue(
            arguments =>
            {
                Assert.Contains($"repos/{Repository}/releases/33", arguments);
                AssertOption(arguments, "--method", "DELETE");
            },
            Success());
        script.Enqueue(RequireReleaseList, Success(ReleasePages()));
        var escrow = CreateEscrow(script);

        await escrow.DeleteDraftAsync("33", CancellationToken.None);

        script.AssertComplete();
    }

    [Fact]
    public async Task IncompleteDraftCanBeDeletedWithoutCleaningUpTheTag()
    {
        var script = new ScriptedGh();
        script.Enqueue(
            RequireReleaseList,
            Success(ReleasePages(Release(32, TagName, VersionCommit, draft: true, immutable: false))));
        script.Enqueue(RequireAssetList, Success(AssetPages()));
        script.Enqueue(
            arguments =>
            {
                Assert.Equal("api", arguments[0]);
                Assert.Contains($"repos/{Repository}/releases/32", arguments);
                AssertOption(arguments, "--method", "DELETE");
                Assert.DoesNotContain("--cleanup-tag", arguments);
            },
            Success());
        script.Enqueue(RequireReleaseList, Success(ReleasePages()));
        var escrow = CreateEscrow(script);

        await escrow.DeleteDraftAsync("32", CancellationToken.None);

        script.AssertComplete();
    }

    [Fact]
    public async Task UploadNeverClobbersAndReconcilesTheExactDigest()
    {
        using var directory = new TemporaryDirectory();
        var bytes = "exact bundle bytes"u8.ToArray();
        var source = Path.Combine(directory.Path, "release-wave.zip");
        await File.WriteAllBytesAsync(source, bytes);

        var script = new ScriptedGh();
        script.Enqueue(
            RequireReleaseList,
            Success(ReleasePages(Release(41, TagName, VersionCommit, draft: true, immutable: false))));
        script.Enqueue(RequireAssetList, Success(AssetPages()));
        script.Enqueue(
            arguments =>
            {
                AssertStartsWith(arguments, "release", "upload", TagName, source);
                AssertOption(arguments, "--repo", Repository);
                Assert.DoesNotContain("--clobber", arguments);
            },
            Failure("simulated response loss"));
        script.Enqueue(
            RequireAssetList,
            Success(AssetPages(Asset(401, "release-wave.zip", bytes))));
        var escrow = CreateEscrow(script);

        await escrow.UploadAssetAsync("41", "release-wave.zip", source, CancellationToken.None);

        script.AssertComplete();
    }

    [Fact]
    public async Task UploadRejectsAnExistingSameNameWithDifferentBytes()
    {
        using var directory = new TemporaryDirectory();
        var source = Path.Combine(directory.Path, "release-wave.zip");
        await File.WriteAllBytesAsync(source, "expected"u8.ToArray());

        var script = new ScriptedGh();
        script.Enqueue(
            RequireReleaseList,
            Success(ReleasePages(Release(42, TagName, VersionCommit, draft: true, immutable: false))));
        script.Enqueue(
            RequireAssetList,
            Success(AssetPages(Asset(402, "release-wave.zip", "different"u8.ToArray()))));
        var escrow = CreateEscrow(script);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            escrow.UploadAssetAsync("42", "release-wave.zip", source, CancellationToken.None));

        Assert.Contains("Refusing to clobber", error.Message, StringComparison.OrdinalIgnoreCase);
        script.AssertComplete();
    }

    [Fact]
    public async Task UploadRefusesToOverwriteAStarterAndRequestsWholeDraftReset()
    {
        using var directory = new TemporaryDirectory();
        var source = Path.Combine(directory.Path, "release-wave.zip");
        await File.WriteAllBytesAsync(source, "expected"u8.ToArray());
        var starter = new
        {
            id = 403,
            name = "release-wave.zip",
            size = 3,
            state = "starter",
            digest = (string?)null
        };

        var script = new ScriptedGh();
        script.Enqueue(
            RequireReleaseList,
            Success(ReleasePages(Release(43, TagName, VersionCommit, draft: true, immutable: false))));
        script.Enqueue(RequireAssetList, Success(AssetPages(starter)));
        var escrow = CreateEscrow(script);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            escrow.UploadAssetAsync("43", "release-wave.zip", source, CancellationToken.None));

        Assert.Contains("starter", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reset and restage", error.Message, StringComparison.OrdinalIgnoreCase);
        script.AssertComplete();
    }

    [Fact]
    public async Task CompletionUploadRecoversAnExactStarterAfterDeleteResponseLoss()
    {
        using var directory = new TemporaryDirectory();
        var bytes = "deterministic completion receipt"u8.ToArray();
        var source = Path.Combine(directory.Path, ReleaseWaveCompletion.FileName);
        await File.WriteAllBytesAsync(source, bytes);
        var starter = new
        {
            id = 404,
            name = ReleaseWaveCompletion.FileName,
            size = 0,
            state = "starter",
            digest = (string?)null
        };

        var script = new ScriptedGh();
        script.Enqueue(
            RequireReleaseList,
            Success(ReleasePages(Release(44, TagName, VersionCommit, draft: true, immutable: false))));
        script.Enqueue(RequireAssetList, Success(AssetPages(starter)));
        script.Enqueue(
            arguments =>
            {
                Assert.Contains($"repos/{Repository}/releases/assets/404", arguments);
                AssertOption(arguments, "--method", "DELETE");
            },
            Failure("simulated delete response loss"));
        script.Enqueue(RequireAssetList, Success(AssetPages()));
        script.Enqueue(
            arguments =>
            {
                AssertStartsWith(arguments, "release", "upload", TagName, source);
                Assert.DoesNotContain("--clobber", arguments);
            },
            Success());
        script.Enqueue(
            RequireAssetList,
            Success(AssetPages(Asset(405, ReleaseWaveCompletion.FileName, bytes))));
        var escrow = CreateEscrow(script);

        await escrow.UploadAssetAsync(
            "44",
            ReleaseWaveCompletion.FileName,
            source,
            CancellationToken.None);

        script.AssertComplete();
    }

    [Fact]
    public async Task DownloadWritesOnlyTheExactAssetAndChecksItsDigest()
    {
        using var directory = new TemporaryDirectory();
        var bytes = "exact escrow bytes"u8.ToArray();
        var destination = Path.Combine(directory.Path, "downloads", "release-wave.zip");
        var script = new ScriptedGh();
        script.Enqueue(
            RequireReleaseList,
            Success(ReleasePages(Release(51, TagName, VersionCommit, draft: true, immutable: false))));
        script.Enqueue(
            RequireAssetList,
            Success(AssetPages(Asset(501, "release-wave.zip", bytes))));
        script.Enqueue(
            arguments =>
            {
                AssertStartsWith(arguments, "release", "download", TagName);
                AssertOption(arguments, "--pattern", "release-wave.zip");
                AssertOption(arguments, "--output", destination);
                Assert.DoesNotContain("--clobber", arguments);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.WriteAllBytes(destination, bytes);
            },
            Success());
        var escrow = CreateEscrow(script);

        await escrow.DownloadAssetAsync(
            "51",
            "release-wave.zip",
            destination,
            CancellationToken.None);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
        script.AssertComplete();
    }

    [Fact]
    public async Task DownloadDeletesBytesThatDisagreeWithGitHubDigest()
    {
        using var directory = new TemporaryDirectory();
        var expected = "expected"u8.ToArray();
        var destination = Path.Combine(directory.Path, "release-wave.zip");
        var script = new ScriptedGh();
        script.Enqueue(
            RequireReleaseList,
            Success(ReleasePages(Release(52, TagName, VersionCommit, draft: true, immutable: false))));
        script.Enqueue(
            RequireAssetList,
            Success(AssetPages(Asset(502, "release-wave.zip", expected))));
        script.Enqueue(
            _ => File.WriteAllBytes(destination, "tampered"u8.ToArray()),
            Success());
        var escrow = CreateEscrow(script);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            escrow.DownloadAssetAsync(
                "52",
                "release-wave.zip",
                destination,
                CancellationToken.None));

        Assert.Contains("digest mismatch", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(destination));
        script.AssertComplete();
    }

    [Fact]
    public async Task AssetListingRejectsCaseAliasesBeforeCoordinatorTrustsNames()
    {
        var bytes = "same"u8.ToArray();
        var script = new ScriptedGh();
        script.Enqueue(
            RequireReleaseList,
            Success(ReleasePages(Release(53, TagName, VersionCommit, draft: true, immutable: false))));
        script.Enqueue(
            RequireAssetList,
            Success(AssetPages(
                Asset(503, "release-wave.zip", bytes),
                Asset(504, "RELEASE-WAVE.ZIP", bytes))));
        var escrow = CreateEscrow(script);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            escrow.ListAssetsAsync("53", CancellationToken.None));

        Assert.Contains("case-aliased", error.Message, StringComparison.OrdinalIgnoreCase);
        script.AssertComplete();
    }

    [Theory]
    [InlineData("pending", "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "unsupported state")]
    [InlineData("uploaded", "md5:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "unsupported digest")]
    public async Task AssetListingRejectsInvalidRemoteMetadata(
        string state,
        string digest,
        string expectedMessage)
    {
        var malformed = new
        {
            id = 505,
            name = "release-wave.zip",
            size = 4,
            state,
            digest
        };
        var script = new ScriptedGh();
        script.Enqueue(
            RequireReleaseList,
            Success(ReleasePages(Release(54, TagName, VersionCommit, draft: true, immutable: false))));
        script.Enqueue(RequireAssetList, Success(AssetPages(malformed)));
        var escrow = CreateEscrow(script);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            escrow.ListAssetsAsync("54", CancellationToken.None));

        Assert.Contains(expectedMessage, error.Message, StringComparison.OrdinalIgnoreCase);
        script.AssertComplete();
    }

    [Fact]
    public async Task AssetListingAllowsMissingDigestWithoutTreatingItAsPublicationProof()
    {
        var legacyShape = new
        {
            id = 506,
            name = "release-wave.zip",
            size = 4,
            state = "uploaded",
            digest = (string?)null
        };
        var script = new ScriptedGh();
        script.Enqueue(
            RequireReleaseList,
            Success(ReleasePages(Release(55, TagName, VersionCommit, draft: true, immutable: false))));
        script.Enqueue(RequireAssetList, Success(AssetPages(legacyShape)));
        var escrow = CreateEscrow(script);

        var asset = Assert.Single(await escrow.ListAssetsAsync("55", CancellationToken.None));

        Assert.Equal("release-wave.zip", asset.Name);
        Assert.Equal(4, asset.Length);
        Assert.True(asset.IsUploaded);
        script.AssertComplete();
    }

    [Fact]
    public async Task StarterAssetIsVisibleButNeverReportedAsUploaded()
    {
        var starter = new
        {
            id = 507,
            name = "release-wave.zip",
            size = 19,
            state = "starter",
            digest = (string?)null
        };
        var script = new ScriptedGh();
        script.Enqueue(
            RequireReleaseList,
            Success(ReleasePages(Release(56, TagName, VersionCommit, draft: true, immutable: false))));
        script.Enqueue(RequireAssetList, Success(AssetPages(starter)));
        var escrow = CreateEscrow(script);

        var asset = Assert.Single(await escrow.ListAssetsAsync("56", CancellationToken.None));

        Assert.Equal(19, asset.Length);
        Assert.False(asset.IsUploaded);
        script.AssertComplete();
    }

    [Fact]
    public async Task AnnotatedTagChainsArePeeledToTheExactCommit()
    {
        var script = new ScriptedGh();
        script.Enqueue(
            RequireMatchingRefs,
            Success(MatchingRefs(
                GitRef($"refs/tags/{TagName}", TagObjectOne, "tag"),
                GitRef($"refs/tags/{TagName}/unrelated", OtherCommit, "commit"))));
        script.Enqueue(
            arguments => Assert.Contains($"repos/{Repository}/git/tags/{TagObjectOne}", arguments),
            Success(TagObject(TagObjectTwo, "tag")));
        script.Enqueue(
            arguments => Assert.Contains($"repos/{Repository}/git/tags/{TagObjectTwo}", arguments),
            Success(TagObject(VersionCommit, "commit")));
        var escrow = CreateEscrow(script);

        var commit = await escrow.ResolveTagTargetAsync(TagName, CancellationToken.None);

        Assert.Equal(VersionCommit, commit);
        script.AssertComplete();
    }

    [Fact]
    public async Task PublishCreatesANonForcedExactTagThenPublishesTheSameDraft()
    {
        var script = new ScriptedGh();
        script.Enqueue(
            RequireReleaseList,
            Success(ReleasePages(Release(61, TagName, VersionCommit, draft: true, immutable: false))));
        script.Enqueue(RequireMatchingRefs, Success(MatchingRefs()));
        script.Enqueue(
            arguments =>
            {
                Assert.Equal("api", arguments[0]);
                Assert.Contains($"repos/{Repository}/git/refs", arguments);
                AssertOption(arguments, "--method", "POST");
                AssertOption(arguments, "--raw-field", $"ref=refs/tags/{TagName}");
                Assert.Contains($"sha={VersionCommit}", arguments);
                Assert.DoesNotContain("--force", arguments);
            },
            Failure("simulated tag-create response loss"));
        script.Enqueue(
            RequireMatchingRefs,
            Success(MatchingRefs(GitRef($"refs/tags/{TagName}", VersionCommit, "commit"))));
        script.Enqueue(
            arguments =>
            {
                Assert.Equal("api", arguments[0]);
                Assert.Contains($"repos/{Repository}/releases/61", arguments);
                AssertOption(arguments, "--method", "PATCH");
                Assert.Contains("draft=false", arguments);
            },
            Failure("simulated publish response loss"));
        script.Enqueue(
            RequireReleaseList,
            Success(ReleasePages(Release(61, TagName, VersionCommit, draft: false, immutable: true))));
        script.Enqueue(
            RequireMatchingRefs,
            Success(MatchingRefs(GitRef($"refs/tags/{TagName}", VersionCommit, "commit"))));
        var escrow = CreateEscrow(script);

        await escrow.PublishAsync("61", TagName, VersionCommit, CancellationToken.None);

        script.AssertComplete();
    }

    [Fact]
    public async Task PublishRefusesToMoveAnExistingWrongTag()
    {
        var script = new ScriptedGh();
        script.Enqueue(
            RequireReleaseList,
            Success(ReleasePages(Release(62, TagName, VersionCommit, draft: true, immutable: false))));
        script.Enqueue(
            RequireMatchingRefs,
            Success(MatchingRefs(GitRef($"refs/tags/{TagName}", OtherCommit, "commit"))));
        var escrow = CreateEscrow(script);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            escrow.PublishAsync("62", TagName, VersionCommit, CancellationToken.None));

        Assert.Contains("Refusing to move or replace", error.Message, StringComparison.OrdinalIgnoreCase);
        script.AssertComplete();
    }

    [Fact]
    public async Task PublishFailsClosedIfGitHubDoesNotMakeTheReleaseImmutable()
    {
        var script = new ScriptedGh();
        script.Enqueue(
            RequireReleaseList,
            Success(ReleasePages(Release(63, TagName, VersionCommit, draft: true, immutable: false))));
        script.Enqueue(
            RequireMatchingRefs,
            Success(MatchingRefs(GitRef($"refs/tags/{TagName}", VersionCommit, "commit"))));
        script.Enqueue(
            arguments =>
            {
                Assert.Contains($"repos/{Repository}/releases/63", arguments);
                AssertOption(arguments, "--method", "PATCH");
            },
            Success());
        script.Enqueue(
            RequireReleaseList,
            Success(ReleasePages(Release(63, TagName, VersionCommit, draft: false, immutable: false))));
        var escrow = CreateEscrow(script);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            escrow.PublishAsync("63", TagName, VersionCommit, CancellationToken.None));

        Assert.Contains("without immutable-release protection", error.Message, StringComparison.OrdinalIgnoreCase);
        script.AssertComplete();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("owner/repo/extra")]
    [InlineData("https://github.com/owner/repo")]
    [InlineData("owner/repo?token=value")]
    public void RepositoryMustBeValidatedOwnerRepo(string repository)
    {
        var script = new ScriptedGh();

        Assert.Throws<ArgumentException>(() =>
            new GitHubReleaseWaveEscrow(
                Directory.GetCurrentDirectory(),
                repository,
                script.RunAsync));
    }

    private static GitHubReleaseWaveEscrow CreateEscrow(ScriptedGh script) =>
        new(Directory.GetCurrentDirectory(), Repository, script.RunAsync);

    private static void RequireReleaseList(IReadOnlyList<string> arguments)
    {
        Assert.Equal("api", arguments[0]);
        Assert.Contains($"repos/{Repository}/releases?per_page=100", arguments);
        Assert.Contains("--paginate", arguments);
        Assert.Contains("--slurp", arguments);
    }

    private static void RequireAssetList(IReadOnlyList<string> arguments)
    {
        Assert.Equal("api", arguments[0]);
        Assert.Contains(arguments, argument => argument.Contains("/assets?per_page=100", StringComparison.Ordinal));
        Assert.Contains("--paginate", arguments);
        Assert.Contains("--slurp", arguments);
    }

    private static void RequireMatchingRefs(IReadOnlyList<string> arguments)
    {
        Assert.Equal("api", arguments[0]);
        Assert.Contains($"repos/{Repository}/git/matching-refs/tags/{TagName}", arguments);
    }

    private static void AssertStartsWith(IReadOnlyList<string> actual, params string[] expected)
    {
        Assert.True(actual.Count >= expected.Length);
        Assert.Equal(expected, actual.Take(expected.Length));
    }

    private static void AssertOption(IReadOnlyList<string> arguments, string option, string value)
    {
        var found = Enumerable.Range(0, arguments.Count - 1).Any(position =>
            string.Equals(arguments[position], option, StringComparison.Ordinal) &&
            string.Equals(arguments[position + 1], value, StringComparison.Ordinal));
        Assert.True(found, $"Expected option '{option} {value}' in: {string.Join(" ", arguments)}");
    }

    private static ProcessResult Success(string output = "") => new(0, output, "");

    private static ProcessResult Failure(string error) => new(1, "", error);

    private static string ReleasePages(params object[] releases) =>
        JsonSerializer.Serialize(new object[] { releases });

    private static object Release(
        long id,
        string tagName,
        string targetCommit,
        bool draft,
        bool immutable) =>
        new
        {
            id,
            tag_name = tagName,
            target_commitish = targetCommit,
            draft,
            immutable,
            prerelease = false
        };

    private static string AssetPages(params object[] assets) =>
        JsonSerializer.Serialize(new object[] { assets });

    private static object Asset(long id, string name, byte[] bytes) =>
        new
        {
            id,
            name,
            size = bytes.LongLength,
            state = "uploaded",
            digest = $"sha256:{Hash(bytes)}"
        };

    private static string MatchingRefs(params object[] refs) => JsonSerializer.Serialize(refs);

    private static object GitRef(string reference, string sha, string type) =>
        new { @ref = reference, @object = new { sha, type } };

    private static string TagObject(string sha, string type) =>
        JsonSerializer.Serialize(new { @object = new { sha, type } });

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class ScriptedGh
    {
        private readonly Queue<Step> steps = new();

        public void Enqueue(Action<IReadOnlyList<string>> verify, ProcessResult result) =>
            steps.Enqueue(new Step(verify, _ => result));

        public Task<ProcessResult> RunAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.True(steps.Count > 0, $"Unexpected gh call: {string.Join(" ", arguments)}");
            var step = steps.Dequeue();
            step.Verify(arguments);
            return Task.FromResult(step.Respond(arguments));
        }

        public void AssertComplete() => Assert.Empty(steps);

        private sealed record Step(
            Action<IReadOnlyList<string>> Verify,
            Func<IReadOnlyList<string>, ProcessResult> Respond);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"koan-github-escrow-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
