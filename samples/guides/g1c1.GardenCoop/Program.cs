using g1c1.GardenCoop.Hosting;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Web.Extensions;
using Koan.Web.Hosting;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateBuilder(args);

builder.ConfigureSampleLogging();

// one line does everything - auto-registration magic!
// finds all entities, controllers, and services automatically
builder.Services.AddKoan();

var app = builder.Build();

// make services available globally - sometimes useful in static helpers
AppHost.Current ??= app.Services;

// seed some test data - plots, members, sensors
await GardenSeederRunner.EnsureSampleDataAsync(app);

// wire up startup/shutdown hooks (browser launcher, etc.)
app.ConfigureSampleLifecycle(
    sampleName: "Garden Cooperative slice",
    startupMessage: "Garden Cooperative slice is listening on {Addresses}. Close the window or press Ctrl+C to stop.",
    shutdownMessage: "Shutting down Garden Cooperative slice – see you at dawn.");

// go!
await app.RunAsync();
