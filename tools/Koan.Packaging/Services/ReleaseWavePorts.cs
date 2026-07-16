using Koan.Packaging.Models;

namespace Koan.Packaging.Services;

internal interface IReleaseWaveEscrow
{
    Task<IReadOnlyList<ReleaseWaveEscrowRelease>> FindByTagIncludingDraftsAsync(
        string tagName,
        CancellationToken cancellationToken);

    Task<ReleaseWaveEscrowRelease> CreateDraftAsync(
        string tagName,
        string targetCommit,
        CancellationToken cancellationToken);

    Task DeleteDraftAsync(string releaseId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReleaseWaveEscrowAsset>> ListAssetsAsync(
        string releaseId,
        CancellationToken cancellationToken);

    Task DownloadAssetAsync(
        string releaseId,
        string assetName,
        string destinationPath,
        CancellationToken cancellationToken);

    Task UploadAssetAsync(
        string releaseId,
        string assetName,
        string sourcePath,
        CancellationToken cancellationToken);

    Task PublishAsync(
        string releaseId,
        string tagName,
        string versionCommit,
        CancellationToken cancellationToken);

    Task<string?> ResolveTagTargetAsync(string tagName, CancellationToken cancellationToken);
}

internal interface IPackagePromotionTarget
{
    Task<bool> ExistsAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken);

    Task PushPackageAsync(
        string packageId,
        string version,
        string packagePath,
        string expectedSha256,
        CancellationToken cancellationToken);

    Task ReplaySymbolsAsync(
        string packageId,
        string version,
        string symbolsPath,
        string expectedSha256,
        CancellationToken cancellationToken);

    Task WaitUntilAvailableAsync(
        string packageId,
        string version,
        CancellationToken cancellationToken);
}
