using g1c1.GardenCoop.Hosting;
using Microsoft.AspNetCore.Builder;
using Koan.Core;
using Koan.Core.Hosting.App;

var builder = WebApplication.CreateBuilder(args);

LoggingConfiguration.Configure(builder);

// one line does everything - auto-registration magic!
// finds all entities, controllers, and services automatically
builder.Services.AddKoan();

var app = builder.Build();

// make services available globally - sometimes useful in static helpers
AppHost.Current ??= app.Services;

// seed some test data - plots, members, sensors
await GardenSeederRunner.EnsureSampleDataAsync(app);

// wire up startup/shutdown hooks (browser launcher, etc.)
ApplicationLifecycle.Configure(app);

// go!
await app.RunAsync();