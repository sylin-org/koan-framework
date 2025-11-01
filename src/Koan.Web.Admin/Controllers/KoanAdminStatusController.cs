using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Admin.Contracts;
using Koan.Admin.Services;
using Koan.Core;
using Koan.Services.Abstractions;
using Koan.Web.Admin.Contracts;
using Koan.Web.Admin.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<KoanAdminStatusController> _logger;

    public KoanAdminStatusController(
        IKoanAdminFeatureManager features,
        IKoanAdminManifestService manifest,
        IServiceProvider serviceProvider,
        ILogger<KoanAdminStatusController> logger)
    {
        _features = features;
        _manifest = manifest;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    [HttpGet("status")]
    public async Task<ActionResult<KoanAdminStatusResponse>> GetStatus([FromQuery] bool? sanitized, CancellationToken cancellationToken)
    {
        var snapshot = _features.Current;
        if (!snapshot.Enabled || !snapshot.WebEnabled)
        {
            return NotFound();
        }

        var sanitizedRequested = sanitized ?? false;
        var runtimeLocked = KoanEnv.IsProduction && !KoanEnv.AllowMagicInProduction;
        var effectiveSanitized = runtimeLocked || sanitizedRequested;
        var runtimeLockReason = runtimeLocked
            ? "Production hosts require sanitized runtime details. Set Koan:AllowMagicInProduction=true to view raw diagnostics."
            : null;

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
                        string.IsNullOrWhiteSpace(s.Label) ? s.Key : s.Label,
                        s.Description ?? string.Empty,
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
        var runtime = KoanAdminRuntimeSurfaceFactory.Capture(effectiveSanitized, runtimeLocked, runtimeLockReason);

        var response = new KoanAdminStatusResponse(KoanEnv.CurrentSnapshot, snapshot, runtime, summary, health, modules, configuration, startupNotes);
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

    [HttpGet("service-mesh")]
    [Produces("application/json")]
    public async Task<ActionResult<KoanAdminServiceMeshSurface>> GetServiceMesh(CancellationToken cancellationToken)
    {
        var snapshot = _features.Current;
        if (!snapshot.Enabled || !snapshot.WebEnabled)
        {
            return NotFound();
        }

        // Check if service mesh is registered
        var serviceMesh = _serviceProvider.GetService<IKoanServiceMesh>();
        if (serviceMesh == null)
        {
            return Ok(KoanAdminServiceMeshSurface.Empty);
        }

        try
        {
            var surface = await KoanAdminServiceMeshSurfaceFactory.CaptureAsync(
                serviceMesh,
                _serviceProvider,
                cancellationToken
            );

            return Ok(surface);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture service mesh surface");
            return StatusCode(500, KoanAdminServiceMeshSurface.Empty);
        }
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
