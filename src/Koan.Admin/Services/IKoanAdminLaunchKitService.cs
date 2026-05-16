using Koan.Admin.Contracts;

namespace Koan.Admin.Services;

public interface IKoanAdminLaunchKitService
{
    Task<KoanAdminLaunchKitMetadata> GetMetadata(CancellationToken cancellationToken = default);

    Task<KoanAdminLaunchKitArchive> GenerateArchive(
        KoanAdminLaunchKitRequest request,
        CancellationToken cancellationToken = default);
}
