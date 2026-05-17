using Koan.Core;
using Koan.Core.Hosting.App;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration["Koan:Environment"] = "Test";
builder.Configuration["Koan:Data:Sources:Default:Adapter"] = "mongo";

builder.Services.AddKoan();
var app = builder.Build();
AppHost.Current ??= app.Services;
await app.RunAsync();

namespace Koan.Web.AdapterSurface.Mongo.Tests
{
    public partial class Program { }
}
