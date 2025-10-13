using Koan.Admin.Contracts;

namespace Koan.Admin.Services;

public interface IKoanAdminLaunchKitService
{
    Task<KoanAdminLaunchKitMetadata> GetMetadataAsync(CancellationToken cancellationToken = default);

    Task<KoanAdminLaunchKitArchive> GenerateArchiveAsync(
        KoanAdminLaunchKitRequest request,
        CancellationToken cancellationToken = default);
}
