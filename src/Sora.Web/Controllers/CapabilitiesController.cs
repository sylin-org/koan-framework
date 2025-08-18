using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Sora.Data.Abstractions;
using Sora.Data.Core;
using Sora.Web.Infrastructure;

namespace Sora.Web.Controllers;

[ApiController]
[Produces("application/json")]
[Route(".well-known/sora/[controller]")]
public sealed class CapabilitiesController(IServiceProvider sp, Sora.Data.Core.IDataService? data) : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        var aggregates = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => SoraWebHelpers.SafeGetTypes(a))
            .Where(t => t.IsClass && !t.IsAbstract)
            .Select(t => new { Type = t, Root = t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntity<>)) })
            .Where(x => x.Root is not null)
            .Select(x => new { x.Type, KeyType = x.Root!.GetGenericArguments()[0] })
            .GroupBy(x => x.Type)
            .Select(g => g.First())
            .ToList();

        var items = aggregates.Select(x =>
        {
            var provider = SoraWebHelpers.ResolveProvider(x.Type, sp);

            QueryCapabilities q = QueryCapabilities.None;
            WriteCapabilities w = WriteCapabilities.None;

            if (data is not null)
            {
                try
                {
                    var repo = SoraWebHelpers.GetRepository(sp, data, x.Type, x.KeyType);
                    if (repo is IQueryCapabilities qc) q = qc.Capabilities;
                    if (repo is IWriteCapabilities wc) w = wc.Writes;
                }
                catch { }
            }

            return new
            {
                type = x.Type.FullName,
                key = SoraWebHelpers.ToKeyName(x.KeyType),
                provider,
                query = SoraWebHelpers.EnumFlags(q),
                write = SoraWebHelpers.EnumFlags(w)
            };
        }).ToArray();

        var payload = new
        {
            aggregates = items,
            links = new[] { new { rel = "observability", href = "/.well-known/sora/observability" } }
        };
        return Ok(payload);
    }
}
