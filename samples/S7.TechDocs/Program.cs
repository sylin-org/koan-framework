using Koan.Core;
using Koan.Core.Modules;
using Koan.Core.Observability;
using Koan.Web.Connector.Swagger;
using Koan.Web.Extensions;
using Koan.Web.Extensions.Authorization;
using S7.TechDocs.Infrastructure;
using S7.TechDocs.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan()
    .AsProxiedApi()
    .WithRateLimit();

builder.Services.AddKoanObservability();
builder.Services.AddKoanSwagger(builder.Configuration);

// Authorization with role-based policies + capability mapping helper
builder.Services.AddKoanAuthorization(
    options =>
    {
        options.AddPolicy("Reader", policy => policy.RequireAuthenticatedUser());
        options.AddPolicy("Author", policy => policy.RequireRole("Author", "Moderator", "Admin"));
        options.AddPolicy("Moderator", policy => policy.RequireRole("Moderator", "Admin"));
        options.AddPolicy("Admin", policy => policy.RequireRole("Admin"));
    },
    opts =>
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
    },
    DevRoleClaims.ApplyAsync);

// Local services
builder.Services.AddSingleton<IDocumentService, DocumentService>();
builder.Services.AddSingleton<ICollectionService, CollectionService>();
builder.Services.AddSingleton<IUserService, UserService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.Logger.LogInformation("Dev role claims transformation enabled.");
}

// TestProvider endpoints are auto-mapped by its auto-registrar when enabled.

// SPA fallback for client-side routing
app.MapFallbackToFile("index.html");

// Swagger UI (enabled in all environments; restrict externally via hosting if needed)
app.UseKoanSwagger();

app.Run();

