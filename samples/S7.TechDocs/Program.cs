using Sora.Core;
using Sora.Core.Modules;
using Sora.Core.Observability;
using Sora.Data.Core;
using Sora.Web.Extensions;
using Sora.Web.Swagger;
using Sora.Web.Auth.Extensions;
using Microsoft.AspNetCore.Authentication;
using Sora.Web.Extensions.Authorization;
using Sora.Data.Mongo;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSora()
    .AsWebApi()
    .AsProxiedApi()
    .WithRateLimit();

builder.Services.AddSoraObservability();
builder.Services.AddSoraSwagger(builder.Configuration);

// Data layer — Single database (Mongo) across environments
// Options auto-bind from configuration; defaults safe for Development
builder.Services.AddMongoAdapter();

// Controllers; Sora.Web.Auth auto-registrar wires authentication
builder.Services.AddControllers();

// Authorization with role-based policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Reader", policy => policy.RequireAuthenticatedUser());
    options.AddPolicy("Author", policy => policy.RequireRole("Author", "Moderator", "Admin"));
    options.AddPolicy("Moderator", policy => policy.RequireRole("Moderator", "Admin"));
    options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
});

// Map Sora capability actions to our role policies so generic controllers can enforce them
builder.Services.AddCapabilityAuthorization(opts =>
{
    // Default mapping for all entities (can be overridden per-entity later)
    opts.Defaults.Moderation.DraftCreate = "Author";   // create/update/get own drafts allowed for Authors+
    opts.Defaults.Moderation.DraftUpdate = "Author";
    opts.Defaults.Moderation.DraftGet = "Author";
    opts.Defaults.Moderation.Submit = "Author";
    opts.Defaults.Moderation.Withdraw = "Author";
    opts.Defaults.Moderation.Queue = "Moderator";      // review queue and actions reserved for Moderators+
    opts.Defaults.Moderation.Approve = "Moderator";
    opts.Defaults.Moderation.Reject = "Moderator";
    opts.Defaults.Moderation.Return = "Moderator";

    // Example: tighten per-entity later via opts.Entities["Document"] = new CapabilityPolicy { ... };
});

// Local services
builder.Services.AddSingleton<S7.TechDocs.Services.IDocumentService, S7.TechDocs.Services.DocumentService>();
builder.Services.AddSingleton<S7.TechDocs.Services.ICollectionService, S7.TechDocs.Services.CollectionService>();
builder.Services.AddSingleton<S7.TechDocs.Services.IUserService, S7.TechDocs.Services.UserService>();

var app = builder.Build();

// Development-only: role enrichment based on cookie (to simulate roles while using TestProvider identity)
if (app.Environment.IsDevelopment())
{
    app.Services.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
        .CreateLogger("Startup").LogInformation("Dev role claims transformation enabled.");
    app.UseMiddleware<S7.TechDocs.Infrastructure.DevRoleClaimsMiddleware>();
}
app.UseAuthorization();

// Static files first, then API routes
app.UseStaticFiles();
app.MapControllers();

// SPA fallback for client-side routing
app.MapFallbackToFile("index.html");

// Swagger UI (enabled in all environments; restrict externally via hosting if needed)
app.UseSoraSwagger();

app.Run();
