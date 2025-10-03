using g1c1.GardenCoop.Infrastructure;
using Microsoft.AspNetCore.Builder;

namespace g1c1.GardenCoop.Hosting;

public static class GardenSeederRunner
{
    public static Task EnsureSampleDataAsync(WebApplication app, CancellationToken cancellationToken = default)
        => GardenSeeder.EnsureSampleDataAsync(app.Services, app.Logger, cancellationToken);
}
