using Koan.Core;
using Koan.Core.Hosting.App;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration["Koan:Environment"] = "Test";
builder.Configuration["Koan:Data:Sources:Default:Adapter"] = "inmemory";
builder.Configuration["Koan:Data:Sources:Default:ConnectionString"] = "memory://sort-tests";

builder.Services.AddKoan();

var app = builder.Build();

AppHost.Current ??= app.Services;

// Koan.Web's WithKoanWeb() / similar is applied via AddKoan auto-discovery.
// We rely on the controllers in this assembly being picked up by reflection.

await app.RunAsync();

namespace Koan.Web.Sort.Tests
{
    public partial class Program { }
}
