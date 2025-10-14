using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Admin.Contracts;
using Koan.Admin.Services;
using Koan.Web.Admin.Contracts;
using Koan.Web.Admin.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Web.Admin.Controllers;

[ApiController]
[Authorize]
[ServiceFilter(typeof(KoanAdminAuthorizationFilter))]
[Route(KoanAdminRouteConvention.ApiPlaceholder)]
[ApiExplorerSettings(GroupName = "KoanAdmin")]
public sealed class KoanAdminStatusController : ControllerBase
{
    private const string SecretMask = "********";
    private readonly IKoanAdminFeatureManager _features;
    private readonly IKoanAdminManifestService _manifest;

    public KoanAdminStatusController(IKoanAdminFeatureManager features, IKoanAdminManifestService manifest)
    {
        _features = features;
        _manifest = manifest;
    }

    [HttpGet("status")]
    public async Task<ActionResult<KoanAdminStatusResponse>> GetStatus(CancellationToken cancellationToken)
    {
        var snapshot = _features.Current;
        if (!snapshot.Enabled || !snapshot.WebEnabled)
        {
            return NotFound();
        }

        var manifest = await _manifest.BuildAsync(cancellationToken).ConfigureAwait(false);
        var summary = manifest.ToSummary();
        var health = manifest.Health;

        var styles = KoanAdminModuleStyleResolver.ResolveAll(manifest.Modules);
        var styleLookup = styles.ToDictionary(style => style.ModuleName, StringComparer.OrdinalIgnoreCase);

        var modules = manifest.Modules
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(m =>
            {
                var style = styleLookup.TryGetValue(m.Name, out var resolvedStyle)
                    ? resolvedStyle
                    : KoanAdminModuleStyleResolver.Resolve(m);

                var settings = m.Settings
                    .Select(s => new KoanAdminModuleSurfaceSetting(
                        s.Key,
                        s.Secret ? SecretMask : s.Value,
                        s.Secret,
                        s.Source,
                        s.SourceKey,
                        s.Consumers.Count == 0 ? Array.Empty<string>() : s.Consumers.ToArray()))
                    .ToList();
                var notes = m.Notes.ToList();
                var tools = m.Tools
                    .Select(t => new KoanAdminModuleSurfaceTool(t.Name, t.Route, t.Description, t.Capability))
                    .ToList();
                return new KoanAdminModuleSurface(
                    m.Name,
                    m.Version,
                    m.Description,
                    m.IsStub,
                    settings,
                    notes,
                    style.Pillar,
                    style.PillarClass,
                    style.ModuleClass,
                    style.Icon,
                    style.ColorHex,
                    style.ColorRgb,
                    tools);
            })
            .ToList();

        var startupNotes = modules
            .SelectMany(m => m.Notes.Select(note => new KoanAdminStartupNote(m.Name, note)))
            .ToList();

        var configuration = BuildConfigurationSummaries(modules);

        var response = new KoanAdminStatusResponse(Koan.Core.KoanEnv.CurrentSnapshot, snapshot, summary, health, modules, configuration, startupNotes);
        return Ok(response);
    }

    [HttpGet("manifest")]
    public async Task<ActionResult<KoanAdminManifest>> GetManifest(CancellationToken cancellationToken)
    {
        var snapshot = _features.Current;
        if (!snapshot.Enabled || !snapshot.WebEnabled)
        {
            return NotFound();
        }

        if (!snapshot.ManifestExposed)
        {
            return Forbid();
        }

        var manifest = await _manifest.BuildAsync(cancellationToken).ConfigureAwait(false);
        return Ok(manifest);
    }

    [HttpGet("health")]
    public async Task<ActionResult<KoanAdminHealthDocument>> GetHealth(CancellationToken cancellationToken)
    {
        var snapshot = _features.Current;
        if (!snapshot.Enabled || !snapshot.WebEnabled)
        {
            return NotFound();
        }

        var health = await _manifest.GetHealthAsync(cancellationToken).ConfigureAwait(false);
        return Ok(health);
    }

    private static KoanAdminConfigurationSummary BuildConfigurationSummaries(IReadOnlyList<KoanAdminModuleSurface> modules)
    {
        if (modules.Count == 0)
        {
            return KoanAdminConfigurationSummary.Empty;
        }

        var summaries = modules
            .GroupBy(module => (module.Pillar, module.PillarClass, module.Icon, module.ColorHex, module.ColorRgb))
            .Select(group => new KoanAdminPillarSummary(
                group.Key.Pillar,
                group.Key.PillarClass,
                group.Key.Icon,
                group.Key.ColorHex,
                group.Key.ColorRgb,
                group.Count(),
                group.Sum(m => m.Settings.Count),
                group.Sum(m => m.Notes.Count)))
            .OrderByDescending(summary => summary.ModuleCount)
            .ThenBy(summary => summary.Pillar, StringComparer.Ordinal)
            .ToList();

        return new KoanAdminConfigurationSummary(summaries);
    }
}
