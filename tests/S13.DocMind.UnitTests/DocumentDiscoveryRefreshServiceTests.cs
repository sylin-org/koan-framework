using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using S13.DocMind.Infrastructure;
using S13.DocMind.Models;
using S13.DocMind.Services;
using Xunit;

namespace S13.DocMind.UnitTests;

public class DocumentDiscoveryRefreshServiceTests
{
    [Fact]
    public async Task EnsureRefreshTracksCompletionMetrics()
    {
        var refresher = new FakeRefresher();
        var service = new DocumentDiscoveryRefreshService(TimeProvider.System, refresher, NullLogger<DocumentDiscoveryRefreshService>.Instance);

        await service.StartAsync(CancellationToken.None).ConfigureAwait(false);
        await service.EnsureRefreshAsync("test", CancellationToken.None).ConfigureAwait(false);
        await WaitForAsync(() => refresher.RefreshCount > 0, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

        var snapshot = service.Snapshot();
        snapshot.TotalCompleted.Should().BeGreaterThanOrEqualTo(1);
        snapshot.PendingCount.Should().Be(0);
        snapshot.LastDuration.Should().NotBeNull();
        snapshot.LastStartedAt.Should().NotBeNull();
    }

    private static async Task WaitForAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (!predicate())
        {
            if (sw.Elapsed > timeout)
            {
                throw new TimeoutException("Condition was not satisfied before timeout");
            }

            await Task.Delay(10).ConfigureAwait(false);
        }
    }

    private sealed class FakeRefresher : IDocumentDiscoveryRefresher
    {
        public int RefreshCount { get; private set; }

        public Task<DocumentDiscoveryProjection> RefreshAsync(CancellationToken cancellationToken)
        {
            RefreshCount++;
            return Task.FromResult(new DocumentDiscoveryProjection
            {
                RefreshedAt = DateTimeOffset.UtcNow
            });
        }

        public Task<DocumentDiscoveryValidationResult> ValidateAsync(DocumentDiscoveryValidationRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new DocumentDiscoveryValidationResult
            {
                ValidatedAt = DateTimeOffset.UtcNow
            });
    }
}
