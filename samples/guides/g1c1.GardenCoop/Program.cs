using g1c1.GardenCoop.Hosting;
using Microsoft.AspNetCore.Builder;
using Koan.Core.Hosting.App;
using Koan.Data.Core;

var builder = WebApplication.CreateBuilder(args);

LoggingConfiguration.Configure(builder);

builder.Services.AddKoan();

var app = builder.Build();

AppHost.Current ??= app.Services;

await GardenSeederRunner.EnsureSampleDataAsync(app);

ApplicationLifecycle.Configure(app);

await app.RunAsync();