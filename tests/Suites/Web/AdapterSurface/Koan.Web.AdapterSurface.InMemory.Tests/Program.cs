using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Web.AdapterSurface.InMemory.Tests.PredicateHook;
using Koan.Web.Hooks;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration["Koan:Environment"] = "Test";
builder.Configuration["Koan:Data:Sources:Default:Adapter"] = "inmemory";
builder.Configuration["Koan:Data:Sources:Default:ConnectionString"] = "memory://adapter-surface";

builder.Services.AddKoan();

// WEB-0068 — register the predicate-contributing hook for the VisibilityWidget surface.
// The hook is generic in TEntity, so registering for VisibilityWidget leaves Widget tests
// untouched.
builder.Services.AddScoped<IRequestOptionsHook<VisibilityWidget>, VisibilityHook>();

var app = builder.Build();
AppHost.Current ??= app.Services;
await app.RunAsync();

namespace Koan.Web.AdapterSurface.InMemory.Tests
{
    public partial class Program { }
}
