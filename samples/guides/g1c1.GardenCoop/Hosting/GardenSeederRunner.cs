using g1c1.GardenCoop.Infrastructure;
using Microsoft.AspNetCore.Builder;

namespace g1c1.GardenCoop.Hosting;

public static class GardenSeederRunner
{
    public static Task EnsureSampleData(WebApplication app, CancellationToken cancellationToken = default)
        => GardenSeeder.EnsureSampleData(app.Services, app.Logger, cancellationToken);
}
