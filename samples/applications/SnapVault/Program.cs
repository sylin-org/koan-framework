using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Web.Extensions;

[assembly: KoanApp(Name = "SnapVault", Code = "snap-vault", Description = "Photo management and AI analysis")]

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddKoan()
    .AsWebApi();

var app = builder.Build();

// Koan owns the API pipeline; the application owns only its SPA shell.
app.MapFallbackToFile("index.html");

await app.RunAsync();
