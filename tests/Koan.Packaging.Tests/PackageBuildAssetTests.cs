using System.IO.Compression;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class PackageBuildAssetTests
{
    private static readonly string[] RequiredAssets =
    [
        PackagingConstants.CoreCompositionTargetPackagePath,
        PackagingConstants.CoreSemanticActivationTargetPackagePath,
        PackagingConstants.CoreRegistryGeneratorPackagePath
    ];

    [Fact]
    public void CorePackageRequiresTheTransitiveSemanticActivationAssets()
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-package-assets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var valid = Path.Combine(root, "valid.nupkg");
            using (var archive = ZipFile.Open(valid, ZipArchiveMode.Create))
            {
                foreach (var asset in RequiredAssets)
                    archive.CreateEntry(asset);
            }

            PackagePipeline.ValidateRequiredBuildAssets(PackagingConstants.CorePackageId, valid);

            for (var missingIndex = 0; missingIndex < RequiredAssets.Length; missingIndex++)
            {
                var missingAsset = RequiredAssets[missingIndex];
                var invalid = Path.Combine(root, $"missing-{missingIndex}.nupkg");
                using (var archive = ZipFile.Open(invalid, ZipArchiveMode.Create))
                {
                    for (var assetIndex = 0; assetIndex < RequiredAssets.Length; assetIndex++)
                    {
                        if (assetIndex != missingIndex)
                            archive.CreateEntry(RequiredAssets[assetIndex]);
                    }
                }

                var exception = Assert.Throws<InvalidOperationException>(() =>
                    PackagePipeline.ValidateRequiredBuildAssets(PackagingConstants.CorePackageId, invalid));
                Assert.Contains(missingAsset, exception.Message, StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CorePackageRejectsIncorrectlyCasedTransitiveBuildAssetPaths()
    {
        var canonicalPath = PackagingConstants.CoreSemanticActivationTargetPackagePath;
        var wrongCasePath = canonicalPath.Replace("buildTransitive", "buildtransitive", StringComparison.Ordinal);
        var root = Path.Combine(Path.GetTempPath(), $"koan-package-assets-case-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var invalid = Path.Combine(root, "wrong-case.nupkg");
            using (var archive = ZipFile.Open(invalid, ZipArchiveMode.Create))
            {
                foreach (var asset in RequiredAssets)
                    archive.CreateEntry(asset == canonicalPath ? wrongCasePath : asset);
            }

            var exception = Assert.Throws<InvalidOperationException>(() =>
                PackagePipeline.ValidateRequiredBuildAssets(PackagingConstants.CorePackageId, invalid));
            Assert.Contains(canonicalPath, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
