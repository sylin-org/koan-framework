using Koan.Core;
using Koan.Core.Modules.Pillars;
using Koan.Core.Observability.Health;
using Koan.Core.Provenance;
using Koan.Web.Admin.Contracts;
using Koan.Web.Admin.Infrastructure;
using Koan.Web.Admin.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Web.Admin.Controllers;

[ApiController]
[ServiceFilter(typeof(KoanAdminAuthorizationFilter))]
[Route(KoanAdminRouteConvention.RootPlaceholder)]
[ApiExplorerSettings(GroupName = "KoanAdmin")]
public sealed class KoanAdminStatusController(
    IOptions<KoanAdminOptions> options,
    IHostEnvironment environment,
    IHealthAggregator? healthAggregator = null) : ControllerBase
{
    private const string SecretMask = "********";

    [HttpGet("status")]
    public ActionResult<KoanAdminStatusResponse> GetStatus(CancellationToken cancellationToken)
    {
        if (!IsActive())
        {
            return NotFound();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var provenance = KoanEnv.Provenance ?? ProvenanceRegistry.Instance.CurrentSnapshot;
        var modules = provenance.Pillars
            .SelectMany(pillar => pillar.Modules.Select(module => MapModule(pillar.Label, module)))
            .OrderBy(module => module.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Ok(new KoanAdminStatusResponse(
            DateTimeOffset.UtcNow,
            KoanEnv.CurrentSnapshot,
            KoanAdminPathUtility.BuildMap(options.Value.PathPrefix),
            KoanAdminRuntimeSurfaceFactory.Capture(),
            BuildHealth(),
            modules));
    }

    [HttpGet("health")]
    public ActionResult<KoanAdminHealthDocument> GetHealth(CancellationToken cancellationToken)
    {
        if (!IsActive())
        {
            return NotFound();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Ok(BuildHealth());
    }

    private bool IsActive() => environment.IsDevelopment() && options.Value.Enabled;

    private KoanAdminHealthDocument BuildHealth()
    {
        if (healthAggregator is null)
        {
            return KoanAdminHealthDocument.Empty;
        }

        try
        {
            var snapshot = healthAggregator.GetSnapshot();
            var components = snapshot.Components
                .Select(component => new KoanAdminHealthComponent(
                    component.Component,
                    component.Status,
                    component.Message,
                    component.TimestampUtc,
                    component.Facts ?? new Dictionary<string, string>()))
                .ToArray();

            return new KoanAdminHealthDocument(snapshot.Overall, components, snapshot.ComputedAtUtc);
        }
        catch
        {
            return KoanAdminHealthDocument.Empty;
        }
    }

    private static KoanAdminModuleSurface MapModule(string pillarLabel, ProvenanceModule module)
    {
        var pillar = KoanPillarCatalog.TryMatchByModuleName(module.Name, out var descriptor)
            ? (descriptor.Label, descriptor.ColorHex, descriptor.Icon)
            : (pillarLabel, "#64748b", "🧩");

        var settings = module.Settings
            .Select(setting => new KoanAdminModuleSurfaceSetting(
                setting.Key,
                string.IsNullOrWhiteSpace(setting.Label) ? setting.Key : setting.Label,
                setting.Description ?? "",
                setting.IsSecret ? SecretMask : setting.Value ?? "",
                setting.IsSecret,
                ConvertSource(setting.Source),
                setting.SourceKey ?? "",
                setting.Consumers.Count == 0 ? [] : setting.Consumers.ToArray()))
            .ToArray();

        var notes = module.Notes.Select(note => note.Message).ToList();
        if (!string.IsNullOrWhiteSpace(module.Status))
        {
            notes.Insert(0, string.IsNullOrWhiteSpace(module.StatusDetail)
                ? module.Status!
                : $"{module.Status}: {module.StatusDetail}");
        }

        var tools = module.Tools
            .Select(tool => new KoanAdminModuleSurfaceTool(tool.Name, tool.Route, tool.Description, tool.Capability))
            .ToArray();

        return new KoanAdminModuleSurface(
            module.Name,
            module.Version,
            string.IsNullOrWhiteSpace(module.Description) ? pillarLabel : module.Description,
            pillar.Item1,
            pillar.Item2,
            pillar.Item3,
            settings,
            notes,
            tools);
    }

    private static KoanAdminSettingSource ConvertSource(ProvenanceSettingSource source)
        => source switch
        {
            ProvenanceSettingSource.Auto => KoanAdminSettingSource.Auto,
            ProvenanceSettingSource.AppSettings => KoanAdminSettingSource.AppSettings,
            ProvenanceSettingSource.Environment => KoanAdminSettingSource.Environment,
            ProvenanceSettingSource.Custom => KoanAdminSettingSource.Custom,
            ProvenanceSettingSource.Unknown => KoanAdminSettingSource.Unknown,
            _ => KoanAdminSettingSource.Custom
        };
}
