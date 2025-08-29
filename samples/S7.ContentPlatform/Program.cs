using Sora.Core.Observability;
using Sora.Data.Core;
using Sora.Messaging;
using Sora.Web.Extensions;
using Sora.Web.Extensions.GenericControllers;
using Sora.Web.Swagger;
using S7.ContentPlatform.Models;
using Sora.Web.Extensions.Authorization;
using Sora.Web.Extensions.Policies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSora()
    .AsWebApi()
    .AsProxiedApi()
    .WithRateLimit();

// Optional: enable OpenTelemetry based on config/env (Sora:Observability or OTEL_* env vars)
builder.Services.AddSoraObservability();

// Wire messaging core for diagnostics surface
builder.Services.AddMessagingCore();

// Register generic capability controllers for our content models
builder.Services
    .AddEntityAuditController<Article>("api/articles")
    .AddEntitySoftDeleteController<Article, string>("api/articles")
    .AddEntityModerationController<Article, string>("api/articles")
    .AddEntityAuditController<Author>("api/authors")
    .AddEntitySoftDeleteController<Author, string>("api/authors");

// Capability authorization: Allow-by-default posture with Defaults and a strict override for Article.Approve
builder.Services.AddCapabilityAuthorization(opts =>
{
    opts.DefaultBehavior = CapabilityDefaultBehavior.Allow;
    opts.Defaults = new CapabilityPolicy
    {
        Moderation = new ModerationPolicy
        {
            DraftCreate = Sora.Web.Extensions.Policies.SoraWebPolicyNames.ModerationAuthor,
            DraftUpdate = Sora.Web.Extensions.Policies.SoraWebPolicyNames.ModerationAuthor,
            DraftGet = Sora.Web.Extensions.Policies.SoraWebPolicyNames.ModerationAuthor,
            Submit = Sora.Web.Extensions.Policies.SoraWebPolicyNames.ModerationAuthor,
            Withdraw = Sora.Web.Extensions.Policies.SoraWebPolicyNames.ModerationAuthor,
            Queue = Sora.Web.Extensions.Policies.SoraWebPolicyNames.ModerationReviewer,
            Approve = Sora.Web.Extensions.Policies.SoraWebPolicyNames.ModerationPublisher,
            Reject = Sora.Web.Extensions.Policies.SoraWebPolicyNames.ModerationReviewer,
            Return = Sora.Web.Extensions.Policies.SoraWebPolicyNames.ModerationReviewer,
        },
        SoftDelete = new SoftDeletePolicy
        {
            ListDeleted = Sora.Web.Extensions.Policies.SoraWebPolicyNames.SoftDeleteActor,
            Delete = Sora.Web.Extensions.Policies.SoraWebPolicyNames.SoftDeleteActor,
            DeleteMany = Sora.Web.Extensions.Policies.SoraWebPolicyNames.SoftDeleteActor,
            Restore = Sora.Web.Extensions.Policies.SoraWebPolicyNames.SoftDeleteActor,
            RestoreMany = Sora.Web.Extensions.Policies.SoraWebPolicyNames.SoftDeleteActor,
        },
        Audit = new AuditPolicy
        {
            Snapshot = Sora.Web.Extensions.Policies.SoraWebPolicyNames.AuditActor,
            List = Sora.Web.Extensions.Policies.SoraWebPolicyNames.AuditActor,
            Revert = Sora.Web.Extensions.Policies.SoraWebPolicyNames.AuditActor,
        }
    };

    opts.Entities[typeof(Article).Name] = new CapabilityPolicy
    {
        Moderation = new ModerationPolicy { Approve = Sora.Web.Extensions.Policies.SoraWebPolicyNames.ModerationPublisher }
    };
});

// Map the simple role-based policies to roles for demo purposes
builder.Services.AddSoraWebCapabilityPolicies(p =>
{
    p.ModerationAuthorRole = "author";
    p.ModerationReviewerRole = "reviewer";
    p.ModerationPublisherRole = "publisher";
    p.SoftDeleteRole = "maintainer";
    p.AuditRole = "auditor";
});

var app = builder.Build();

// Enable Swagger UI per policy: Dev by default; in non-dev only when Sora__Web__Swagger__Enabled=true or SORA_MAGIC_ENABLE_SWAGGER=true
app.UseSoraSwagger();

// Ensure local data folder exists for providers that default to ./data
if (app.Environment.IsDevelopment())
{
    var dataPath = Path.Combine(app.Environment.ContentRootPath, "data");
    try { Directory.CreateDirectory(dataPath); } catch { /* best effort */ }
}

app.Run();

namespace S7.ContentPlatform
{
    public partial class Program { }
}
