using Koan.Core.Observability;
using Koan.Data.Core;
using Koan.Messaging;
using Koan.Web.Extensions;
using Koan.Web.Extensions.GenericControllers;
using Koan.Web.Connector.Swagger;
using S7.ContentPlatform.Models;
using Koan.Web.Extensions.Authorization;
using Koan.Web.Extensions.Policies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddKoan()
    .AsProxiedApi()
    .WithRateLimit();

// Optional: enable OpenTelemetry based on config/env (Koan:Observability or OTEL_* env vars)
builder.Services.AddKoanObservability();

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
            DraftCreate = Koan.Web.Extensions.Policies.KoanWebPolicyNames.ModerationAuthor,
            DraftUpdate = Koan.Web.Extensions.Policies.KoanWebPolicyNames.ModerationAuthor,
            DraftGet = Koan.Web.Extensions.Policies.KoanWebPolicyNames.ModerationAuthor,
            Submit = Koan.Web.Extensions.Policies.KoanWebPolicyNames.ModerationAuthor,
            Withdraw = Koan.Web.Extensions.Policies.KoanWebPolicyNames.ModerationAuthor,
            Queue = Koan.Web.Extensions.Policies.KoanWebPolicyNames.ModerationReviewer,
            Approve = Koan.Web.Extensions.Policies.KoanWebPolicyNames.ModerationPublisher,
            Reject = Koan.Web.Extensions.Policies.KoanWebPolicyNames.ModerationReviewer,
            Return = Koan.Web.Extensions.Policies.KoanWebPolicyNames.ModerationReviewer,
        },
        SoftDelete = new SoftDeletePolicy
        {
            ListDeleted = Koan.Web.Extensions.Policies.KoanWebPolicyNames.SoftDeleteActor,
            Delete = Koan.Web.Extensions.Policies.KoanWebPolicyNames.SoftDeleteActor,
            DeleteMany = Koan.Web.Extensions.Policies.KoanWebPolicyNames.SoftDeleteActor,
            Restore = Koan.Web.Extensions.Policies.KoanWebPolicyNames.SoftDeleteActor,
            RestoreMany = Koan.Web.Extensions.Policies.KoanWebPolicyNames.SoftDeleteActor,
        },
        Audit = new AuditPolicy
        {
            Snapshot = Koan.Web.Extensions.Policies.KoanWebPolicyNames.AuditActor,
            List = Koan.Web.Extensions.Policies.KoanWebPolicyNames.AuditActor,
            Revert = Koan.Web.Extensions.Policies.KoanWebPolicyNames.AuditActor,
        }
    };

    opts.Entities[typeof(Article).Name] = new CapabilityPolicy
    {
        Moderation = new ModerationPolicy { Approve = Koan.Web.Extensions.Policies.KoanWebPolicyNames.ModerationPublisher }
    };
});

// Map the simple role-based policies to roles for demo purposes
builder.Services.AddKoanWebCapabilityPolicies(p =>
{
    p.ModerationAuthorRole = "author";
    p.ModerationReviewerRole = "reviewer";
    p.ModerationPublisherRole = "publisher";
    p.SoftDeleteRole = "maintainer";
    p.AuditRole = "auditor";
});

var app = builder.Build();

// Enable Swagger UI per policy: Dev by default; in non-dev only when Koan__Web__Swagger__Enabled=true or Koan_MAGIC_ENABLE_SWAGGER=true
app.UseKoanSwagger();

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

