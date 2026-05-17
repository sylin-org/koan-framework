using Koan.Core;
using Koan.Core.Hosting.App;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration["Koan:Environment"] = "Development";
builder.Configuration["Koan:Data:Sources:Default:Adapter"] = "sqlite";

builder.Services.AddKoan();
var app = builder.Build();
AppHost.Current ??= app.Services;
await app.RunAsync();

namespace Koan.Web.AdapterSurface.Sqlite.Tests { public partial class Program { } }
