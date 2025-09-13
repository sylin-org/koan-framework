using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Core.Extensions;

namespace Sora.Data.Core.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Data.Core";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Register the generic RelationshipEnricher for all Entity<> types
        services.AddSingleton(typeof(Sora.Data.Core.Enrichment.IEntityEnricher<>), typeof(Sora.Data.Core.Enrichment.RelationshipEnricher<>));
    }

    public void Describe(Sora.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var ensure = cfg.Read(Infrastructure.Constants.Configuration.Runtime.EnsureSchemaOnStart, true);
        report.AddSetting("EnsureSchemaOnStart", ensure.ToString());
            // Report discovered relationships
            var relMeta = new Sora.Data.Core.Relationships.RelationshipMetadataService();
            var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .ToList();
            foreach (var t in allTypes)
            {
                var parents = relMeta.GetParentRelationships(t);
                if (parents.Count > 0)
                {
                    var parentList = string.Join(", ", parents.Select(p => $"{p.ParentType.Name} via {p.PropertyName}"));
                    report.AddNote($"Entity {t.Name} parents: {parentList}");
                }
                var children = relMeta.GetChildRelationships(t);
                if (children.Count > 0)
                {
                    var childList = string.Join(", ", children.Select(c => $"{c.ChildType.Name} via {c.PropertyName}"));
                    report.AddNote($"Entity {t.Name} children: {childList}");
                }
            }
    }
}
