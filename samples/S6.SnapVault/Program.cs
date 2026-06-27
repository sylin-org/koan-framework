using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Web.Extensions;

[assembly: KoanApp(Name = "SnapVault", Code = "snap-vault", Description = "Photo management and AI analysis")]

var builder = WebApplication.CreateBuilder(args);

// Koan Framework — "Reference = Intent": AddKoan() discovers every referenced module (ZenGarden, Jobs,
// Tenancy, Media, Storage, AI, Vector) and SnapVaultModule. AsWebApi() wires the web surface.
builder.Services
    .AddKoan()
    .AsWebApi();

var app = builder.Build();

// Set AppHost.Current so entity ops + the ZenGarden advisor work during host startup (SnapVaultModule.Start).
AppHost.Current ??= app.Services;

// The framework's KoanWebStartupFilter (wired by AsWebApi()) already owns the pipeline: static files,
// routing, auth, the AfterAuthentication stage (where Koan.Identity.Tenancy mounts in step 2), and
// MapControllers via UseEndpoints. The only app-owned endpoint is the SPA shell fallback, which the filter
// does not map. UseStaticFiles is kept (harmless second instance) to match the proven SPA sample shape.
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

await app.RunAsync();
