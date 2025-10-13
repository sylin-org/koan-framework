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

        var modules = manifest.Modules
            .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(m =>
            {
                var settings = m.Settings
                    .Select(s => new KoanAdminModuleSurfaceSetting(s.Key, s.Secret ? SecretMask : s.Value, s.Secret))
                    .ToList();
                var notes = m.Notes.ToList();
                return new KoanAdminModuleSurface(m.Name, m.Version, settings, notes);
            })
            .ToList();

        var startupNotes = modules
            .SelectMany(m => m.Notes.Select(note => new KoanAdminStartupNote(m.Name, note)))
            .ToList();

        var response = new KoanAdminStatusResponse(Koan.Core.KoanEnv.CurrentSnapshot, snapshot, summary, health, modules, startupNotes);
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
}
