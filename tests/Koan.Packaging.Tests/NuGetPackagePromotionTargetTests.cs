using System.Security.Cryptography;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class NuGetPackagePromotionTargetTests
{
    [Fact]
    public void PackagePushKeepsNupkgAndSymbolsAsSeparateTransitions()
    {
        const string packagePath = "artifacts/Sylin.Koan.Test.1.2.3.nupkg";
        const string credential = "test-credential";

        var arguments = NuGetPackagePromotionTarget.BuildPackagePushArguments(
            packagePath,
            credential);

        Assert.Equal(
            [
                "nuget",
                "push",
                packagePath,
                "--source",
                PackagingConstants.NuGetSource,
                "--api-key",
                credential,
                "--skip-duplicate",
                "--no-symbols",
                "--timeout",
                "300"
            ],
            arguments);
    }

    [Fact]
    public void SymbolReplayIsExplicitAndIdempotent()
    {
        const string symbolsPath = "artifacts/Sylin.Koan.Test.1.2.3.snupkg";
        const string credential = "test-credential";

        var arguments = NuGetPackagePromotionTarget.BuildSymbolsPushArguments(
            symbolsPath,
            credential);

        Assert.Equal(
            [
                "nuget",
                "push",
                symbolsPath,
                "--source",
                PackagingConstants.NuGetSource,
                "--api-key",
                credential,
                "--skip-duplicate",
                "--timeout",
                "300"
            ],
            arguments);
        Assert.DoesNotContain("--no-symbols", arguments);
    }

    [Fact]
    public void FailedPushDiagnosticsPreserveContextAndRedactTheCredential()
    {
        const string credential = "exact-short-lived-credential";
        const string diagnostics =
            "NuGet push failed for --api-key exact-short-lived-credential. " +
            "Retry exact-short-lived-credential after the service recovers.";

        var sanitized = NuGetPackagePromotionTarget.SanitizeProcessDiagnostics(
            diagnostics,
            credential);

        Assert.DoesNotContain(credential, sanitized, StringComparison.Ordinal);
        Assert.Equal(2, sanitized.Split("[redacted]", StringSplitOptions.None).Length - 1);
        Assert.Contains("NuGet push failed", sanitized, StringComparison.Ordinal);
        Assert.Contains("service recovers", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HashGuardAcceptsOnlyThePreparedArtifactBytes()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"koan-nuget-promotion-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "Sylin.Koan.Test.1.2.3.nupkg");
            await File.WriteAllBytesAsync(path, "prepared bytes"u8.ToArray());
            var expected = Convert.ToHexString(
                    SHA256.HashData(await File.ReadAllBytesAsync(path)))
                .ToLowerInvariant();

            await NuGetPackagePromotionTarget.VerifyArtifactHashAsync(
                path,
                expected.ToUpperInvariant(),
                "Sylin.Koan.Test/1.2.3",
                CancellationToken.None);

            await File.AppendAllTextAsync(path, "tampered");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                NuGetPackagePromotionTarget.VerifyArtifactHashAsync(
                    path,
                    expected,
                    "Sylin.Koan.Test/1.2.3",
                    CancellationToken.None));

            Assert.Contains("hash mismatch", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Sylin.Koan.Test/1.2.3", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task HashGuardRejectsMissingArtifactBeforePromotion()
    {
        var missing = Path.Combine(
            Path.GetTempPath(),
            $"koan-missing-{Guid.NewGuid():N}.snupkg");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NuGetPackagePromotionTarget.VerifyArtifactHashAsync(
                missing,
                new string('0', 64),
                "Sylin.Koan.Test/1.2.3 symbols",
                CancellationToken.None));

        Assert.Contains("missing", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("symbols", error.Message, StringComparison.Ordinal);
    }
}
