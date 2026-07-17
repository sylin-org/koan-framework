using DevPortal.Models;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Koan.Web.OpenGraph;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DevPortal.Initialization;

/// <summary>Declares DevPortal's application-owned composition.</summary>
public sealed class DevPortalModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        SocialCards.For<Article>("/articles/{id}", id => Article.Get(id))
            .Title(article => article.Title)
            .Description(article => article.Summary)
            .Url(article => $"/articles/{article.Id}")
            .Type("article");
    }

    public override void Report(ProvenanceModuleWriter module, IConfiguration configuration, IHostEnvironment environment)
    {
        module.Describe(Version, "Editorial publication and entity-backed social cards.");
        module.AddNote("Article navigations receive share metadata from the current editorial entity.");
    }
}
