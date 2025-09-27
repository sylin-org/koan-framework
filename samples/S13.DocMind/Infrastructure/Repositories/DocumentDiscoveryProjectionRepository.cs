using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Core;
using S13.DocMind.Models;

namespace S13.DocMind.Infrastructure.Repositories;

public static class DocumentDiscoveryProjectionRepository
{
    private const string ProjectionId = "global";

    public static Task<DocumentDiscoveryProjection?> GetAsync(CancellationToken cancellationToken)
        => DocumentDiscoveryProjection.Get(ProjectionId, cancellationToken);

    public static async Task<DocumentDiscoveryProjection> SaveAsync(DocumentDiscoveryProjection projection, CancellationToken cancellationToken)
    {
        projection.Id = ProjectionId;
        projection.Scope = ProjectionId;
        projection.RefreshedAt = projection.RefreshedAt == default ? DateTimeOffset.UtcNow : projection.RefreshedAt;
        return await projection.Save(cancellationToken).ConfigureAwait(false);
    }
}
