using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Data.Core;
using S16.PantryPal.Models;

namespace S16.PantryPal.Services;

/// <summary>
/// Flight-once seed service: executes a single time on host start, inserting demo pantry items if store is empty.
/// Safe to keep idempotent: checks for any existing PantryItem before seeding.
/// </summary>
public sealed class PantrySeedHostedService : IHostedService
{
    private readonly ILogger<PantrySeedHostedService>? _logger;
    private readonly IngestionOptions _opts; // potential future use (e.g., default expirations)
    private static int _executed; // ensure single run

    public PantrySeedHostedService(ILogger<PantrySeedHostedService>? logger, IOptions<IngestionOptions> opts)
    {
        _logger = logger;
        _opts = opts.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _executed, 1, 0) != 0)
        {
            return; // already executed
        }
        try
        {
            var existing = await PantryItem.FirstPage(1, cancellationToken);
            if (existing.Count > 0)
            {
                _logger?.LogInformation("Pantry seed skipped (items already exist)");
                return;
            }
            var now = DateTime.UtcNow;
            var seeds = new List<PantryItem>
            {
                new() { Name = "Milk", Category = "dairy", Status = "available", ExpiresAt = now.AddDays(7), Quantity = 1, Unit = "carton" },
                new() { Name = "Carrots", Category = "produce", Status = "available", ExpiresAt = now.AddDays(5), Quantity = 6, Unit = "pcs" },
                new() { Name = "Bread", Category = "bakery", Status = "available", ExpiresAt = now.AddDays(3), Quantity = 1, Unit = "loaf" },
                new() { Name = "Chicken Breast", Category = "meat", Status = "available", ExpiresAt = now.AddDays(3), Quantity = 4, Unit = "pcs" },
                new() { Name = "Apples", Category = "produce", Status = "available", ExpiresAt = now.AddDays(10), Quantity = 5, Unit = "pcs" }
            };
            foreach (var s in seeds)
                await s.Save();
            _logger?.LogInformation("Pantry seed inserted {Count} items", seeds.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Pantry seed failed");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
