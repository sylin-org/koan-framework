using System.IO.Compression;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class PackageBuildAssetTests
{
    [Fact]
    public void CorePackageRequiresTheTransitiveCompositionTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), $"koan-package-assets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var valid = Path.Combine(root, "valid.nupkg");
            using (var archive = ZipFile.Open(valid, ZipArchiveMode.Create))
                archive.CreateEntry(PackagingConstants.CoreCompositionTargetPackagePath);

            PackagePipeline.ValidateRequiredBuildAssets(PackagingConstants.CorePackageId, valid);

            var invalid = Path.Combine(root, "invalid.nupkg");
            using (var archive = ZipFile.Open(invalid, ZipArchiveMode.Create))
                archive.CreateEntry("build/Sylin.Koan.Core.targets");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                PackagePipeline.ValidateRequiredBuildAssets(PackagingConstants.CorePackageId, invalid));
            Assert.Contains("transitive build asset", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
