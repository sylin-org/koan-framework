using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Koan.Canon.Model;
using System.Reflection;

namespace Koan.Canon.Web.Controllers;

[ApiController]
[Route("views")] // /views/{view}/{ReferenceId}
public sealed class ViewsController : ControllerBase
{
    private readonly ILogger<ViewsController> _logger;

    public ViewsController(ILogger<ViewsController> logger)
    {
        _logger = logger;
    }

    [HttpGet("{view}/{referenceId}")]
    public async Task<IActionResult> GetOne([FromRoute] string view, [FromRoute] string referenceId, CancellationToken ct)
    {
        // Ensure we target the correct per-model view set (Canon.views.<view>)
        var set = Koan.Canon.Infrastructure.CanonSets.ViewShort(view);
        // Prefer direct Get by document id to avoid dependency on string query support
        static async Task<object?> TryGetByIdAsync(Type projectionType, string fullId, string set, CancellationToken ct)
        {
            var dataType = typeof(Koan.Data.Core.Data<,>).MakeGenericType(projectionType, typeof(string));
            var getM = dataType.GetMethod("GetAsync", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(string), typeof(CancellationToken) })!;
            var t = (Task)getM.Invoke(null, new object?[] { fullId, set, ct })!;
            await t.ConfigureAwait(false);
            return t.GetType().GetProperty("Result")!.GetValue(t);
        }
        if (string.Equals(view, Koan.Canon.Infrastructure.Constants.Views.Canonical, StringComparison.OrdinalIgnoreCase))
        {
            // First try direct id fetch for each model
            foreach (var modelType in DiscoverModels())
            {
                var canType = typeof(Koan.Canon.Model.CanonicalProjection<>).MakeGenericType(modelType);
                var raw = await TryGetByIdAsync(canType, $"{Koan.Canon.Infrastructure.Constants.Views.Canonical}::{referenceId}", set, ct);
                if (raw is not null)
                {
                    var viewDoc = new CanonicalProjectionView
                    {
                        Id = (string)canType.GetProperty("Id")!.GetValue(raw)!,
                        ReferenceId = (string?)(canType.GetProperty("ReferenceId")?.GetValue(raw)),
                        ViewName = (string)canType.GetProperty("ViewName")!.GetValue(raw)!,
                        Model = canType.GetProperty("Model")?.GetValue(raw) ?? canType.GetProperty("View")!.GetValue(raw)
                    };
                    _logger.LogInformation("ViewsController.GetOne Canonical(view,id) view={View} ref={Ref} found=true", view, referenceId);
                    return Ok(viewDoc);
                }
            }
            var list = await QueryCanonicalAcrossModels($"ReferenceId == '{referenceId}'", set, ct);
            var doc = list.FirstOrDefault();
            _logger.LogInformation("ViewsController.GetOne Canonical view={View} ref={Ref} found={Found}", view, referenceId, doc is not null);
            return doc is null ? NotFound() : Ok(doc);
        }
        if (string.Equals(view, Koan.Canon.Infrastructure.Constants.Views.Lineage, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var modelType in DiscoverModels())
            {
                var linType = typeof(Koan.Canon.Model.LineageProjection<>).MakeGenericType(modelType);
                var raw = await TryGetByIdAsync(linType, $"{Koan.Canon.Infrastructure.Constants.Views.Lineage}::{referenceId}", set, ct);
                if (raw is not null)
                {
                    var viewDoc = new LineageProjectionView
                    {
                        Id = (string)linType.GetProperty("Id")!.GetValue(raw)!,
                        ReferenceId = (string?)(linType.GetProperty("ReferenceId")?.GetValue(raw)),
                        ViewName = (string)linType.GetProperty("ViewName")!.GetValue(raw)!,
                        View = (Dictionary<string, Dictionary<string, string[]>>)linType.GetProperty("View")!.GetValue(raw)!
                    };
                    _logger.LogInformation("ViewsController.GetOne lineage(view,id) view={View} ref={Ref} found=true", view, referenceId);
                    return Ok(viewDoc);
                }
            }
            var list = await QueryLineageAcrossModels($"ReferenceId == '{referenceId}'", set, ct);
            var doc = list.FirstOrDefault();
            _logger.LogInformation("ViewsController.GetOne lineage view={View} ref={Ref} found={Found}", view, referenceId, doc is not null);
            return doc is null ? NotFound() : Ok(doc);
        }

        // Fallback generic: aggregate across all models for arbitrary views (e.g., latest.reading, window.5m)
        var any = await QueryGenericAcrossModels(view, $"ReferenceId == '{referenceId}'", set, ct);
        var gdoc = any.FirstOrDefault();
        _logger.LogInformation("ViewsController.GetOne generic(view,all) view={View} ref={Ref} found={Found}", view, referenceId, gdoc is not null);
        return gdoc is null ? NotFound() : Ok(gdoc);
    }

    [HttpGet("{view}")]
    public async Task<IActionResult> GetPage([FromRoute] string view, [FromQuery] string? q, [FromQuery] int? page = 1, [FromQuery] int? size = 50, CancellationToken ct = default)
    {
        var p = Math.Max(1, page ?? 1);
        var s = Math.Clamp(size ?? 50, 1, 500);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var set = Koan.Canon.Infrastructure.CanonSets.ViewShort(view);
            // Fast-path: support simple ReferenceId equality without relying on string query provider
            string? refFilter = null;
            try
            {
                // Accept forms like ReferenceId == '01HF...' or ReferenceId=="01HF..." (very simple parser)
                var normalized = q!.Trim();
                if (normalized.StartsWith("ReferenceId", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = normalized.IndexOf("==", StringComparison.Ordinal);
                    if (idx > 0)
                    {
                        var val = normalized.Substring(idx + 2).Trim();
                        if (val.Length >= 2 && ((val[0] == '\'' && val[^1] == '\'') || (val[0] == '"' && val[^1] == '"')))
                        {
                            refFilter = val.Substring(1, val.Length - 2);
                        }
                    }
                }
            }
            catch { }
            // Filter within the per-view set using the correct projection type
            if (string.Equals(view, Koan.Canon.Infrastructure.Constants.Views.Canonical, StringComparison.OrdinalIgnoreCase))
            {
                var results = refFilter is null ? await QueryCanonicalAcrossModels(q!, set, ct)
                                                : (await QueryCanonicalAcrossModels(null, set, ct)).Where(x => string.Equals(x.ReferenceId, refFilter, StringComparison.OrdinalIgnoreCase)).ToList();
                var total = results.Count;
                var skip = (p - 1) * s;
                var pageItems = results.Skip(skip).Take(s).ToList();
                var hasNext = skip + pageItems.Count < total;
                _logger.LogInformation("ViewsController.GetPage Canonical view={View} q={Query} total={Total} page={Page} size={Size} returned={Returned}", view, q, total, p, s, pageItems.Count);
                return Ok(new { page = p, size = s, total, hasNext, items = pageItems });
            }
            if (string.Equals(view, Koan.Canon.Infrastructure.Constants.Views.Lineage, StringComparison.OrdinalIgnoreCase))
            {
                var results = refFilter is null ? await QueryLineageAcrossModels(q!, set, ct)
                                                : (await QueryLineageAcrossModels(null, set, ct)).Where(x => string.Equals(x.ReferenceId, refFilter, StringComparison.OrdinalIgnoreCase)).ToList();
                var total = results.Count;
                var skip = (p - 1) * s;
                var pageItems = results.Skip(skip).Take(s).ToList();
                var hasNext = skip + pageItems.Count < total;
                _logger.LogInformation("ViewsController.GetPage lineage view={View} q={Query} total={Total} page={Page} size={Size} returned={Returned}", view, q, total, p, s, pageItems.Count);
                return Ok(new { page = p, size = s, total, hasNext, items = pageItems });
            }

            // Fallback generic: aggregate across models
            var agg = await QueryGenericAcrossModels(view, q!, set, ct);
            var gtotal = agg.Count;
            var gskip = (p - 1) * s;
            var gitems = agg.Skip(gskip).Take(s).ToList();
            var gnext = gskip + gitems.Count < gtotal;
            _logger.LogInformation("ViewsController.GetPage generic(view,all) view={View} q={Query} total={Total} page={Page} size={Size} returned={Returned}", view, q, gtotal, p, s, gitems.Count);
            return Ok(new { page = p, size = s, total = gtotal, hasNext = gnext, items = gitems });
        }

        // Unfiltered: aggregate across all model view sets and page in-memory (small data expected for this endpoint)
        if (string.Equals(view, Koan.Canon.Infrastructure.Constants.Views.Canonical, StringComparison.OrdinalIgnoreCase))
        {
            var CanonicalAll = await QueryCanonicalAcrossModels(null, Koan.Canon.Infrastructure.CanonSets.ViewShort(view), ct);
            var total = CanonicalAll.Count;
            var skip = (p - 1) * s;
            var pageItems = CanonicalAll.Skip(skip).Take(s).ToList();
            var hasNext = skip + pageItems.Count < total;
            _logger.LogInformation("ViewsController.GetPage Canonical(view,all) view={View} total={Total} page={Page} size={Size} returned={Returned}", view, total, p, s, pageItems.Count);
            return Ok(new { page = p, size = s, total, hasNext, items = pageItems });
        }
        if (string.Equals(view, Koan.Canon.Infrastructure.Constants.Views.Lineage, StringComparison.OrdinalIgnoreCase))
        {
            var lineageAll = await QueryLineageAcrossModels(null, Koan.Canon.Infrastructure.CanonSets.ViewShort(view), ct);
            var total = lineageAll.Count;
            var skip = (p - 1) * s;
            var pageItems = lineageAll.Skip(skip).Take(s).ToList();
            var hasNext = skip + pageItems.Count < total;
            _logger.LogInformation("ViewsController.GetPage lineage(view,all) view={View} total={Total} page={Page} size={Size} returned={Returned}", view, total, p, s, pageItems.Count);
            return Ok(new { page = p, size = s, total, hasNext, items = pageItems });
        }

        // Fallback generic: aggregate across all models for arbitrary views
        var genericAll = await QueryGenericAcrossModels(view, null, Koan.Canon.Infrastructure.CanonSets.ViewShort(view), ct);
        var totalAll = genericAll.Count;
        var skipAll = (p - 1) * s;
        var itemsAll = genericAll.Skip(skipAll).Take(s).ToList();
        var nextAll = skipAll + itemsAll.Count < totalAll;
        _logger.LogInformation("ViewsController.GetPage generic(view,all) view={View} total={Total} page={Page} size={Size} returned={Returned}", view, totalAll, p, s, itemsAll.Count);
        return Ok(new { page = p, size = s, total = totalAll, hasNext = nextAll, items = itemsAll });
    }

    // Helpers: aggregate typed view documents across all discovered Canon models and coerce into untyped view DTOs
    private static List<Type> DiscoverModels()
    {
        var result = new List<Type>();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var asm in assemblies)
        {
            Type?[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
            catch { continue; }
            foreach (var t in types)
            {
                if (t is null || !t.IsClass || t.IsAbstract) continue;
                var bt = t.BaseType;
                if (bt is null || !bt.IsGenericType) continue;
                if (bt.GetGenericTypeDefinition() != typeof(Koan.Canon.Model.CanonEntity<>)) continue;
                result.Add(t);
            }
        }
        return result;
    }

    private static async Task<List<CanonicalProjectionView>> QueryCanonicalAcrossModels(string? q, string set, CancellationToken ct)
    {
        var acc = new List<CanonicalProjectionView>();
        foreach (var modelType in DiscoverModels())
        {
            var canType = typeof(Koan.Canon.Model.CanonicalProjection<>).MakeGenericType(modelType);
            var dataType = typeof(Koan.Data.Core.Data<,>).MakeGenericType(canType, typeof(string));
            Task task;
            if (!string.IsNullOrWhiteSpace(q))
            {
                var qM = dataType.GetMethod("Query", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(string), typeof(CancellationToken) })!;
                task = (Task)qM.Invoke(null, new object?[] { q!, set, ct })!;
            }
            else
            {
                var allM = dataType.GetMethod("All", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(CancellationToken) })!;
                task = (Task)allM.Invoke(null, new object?[] { set, ct })!;
            }
            await task.ConfigureAwait(false);
            var result = (System.Collections.IEnumerable?)GetTaskResult(task);
            if (result is null) continue;
            foreach (var it in result)
            {
                var doc = new CanonicalProjectionView
                {
                    Id = (string)canType.GetProperty("Id")!.GetValue(it)!,
                    ReferenceId = (string?)(canType.GetProperty("ReferenceId")?.GetValue(it)),
                    ViewName = (string)canType.GetProperty("ViewName")!.GetValue(it)!,
                    Model = canType.GetProperty("Model")?.GetValue(it) ?? canType.GetProperty("View")!.GetValue(it)
                };
                acc.Add(doc);
            }
        }
        return acc;
    }

    private static async Task<List<LineageProjectionView>> QueryLineageAcrossModels(string? q, string set, CancellationToken ct)
    {
        var acc = new List<LineageProjectionView>();
        foreach (var modelType in DiscoverModels())
        {
            var linType = typeof(Koan.Canon.Model.LineageProjection<>).MakeGenericType(modelType);
            var dataType = typeof(Koan.Data.Core.Data<,>).MakeGenericType(linType, typeof(string));
            Task task;
            if (!string.IsNullOrWhiteSpace(q))
            {
                var qM = dataType.GetMethod("Query", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(string), typeof(CancellationToken) })!;
                task = (Task)qM.Invoke(null, new object?[] { q!, set, ct })!;
            }
            else
            {
                var allM = dataType.GetMethod("All", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(CancellationToken) })!;
                task = (Task)allM.Invoke(null, new object?[] { set, ct })!;
            }
            await task.ConfigureAwait(false);
            var result = (System.Collections.IEnumerable?)GetTaskResult(task);
            if (result is null) continue;
            foreach (var it in result)
            {
                var doc = new LineageProjectionView
                {
                    Id = (string)linType.GetProperty("Id")!.GetValue(it)!,
                    ReferenceId = (string?)(linType.GetProperty("ReferenceId")?.GetValue(it)),
                    ViewName = (string)linType.GetProperty("ViewName")!.GetValue(it)!,
                    View = (Dictionary<string, Dictionary<string, string[]>>)linType.GetProperty("View")!.GetValue(it)!
                };
                acc.Add(doc);
            }
        }
        return acc;
    }


    private static object? GetTaskResult(Task t)
    {
        var type = t.GetType();
        if (type.IsGenericType) return type.GetProperty("Result")!.GetValue(t);
        return null;
    }

    // Aggregate arbitrary projection views (ProjectionView<TModel, object>) across all discovered models
    private static async Task<List<Koan.Canon.Model.ProjectionView<object>>> QueryGenericAcrossModels(string viewName, string? q, string set, CancellationToken ct)
    {
        var acc = new List<Koan.Canon.Model.ProjectionView<object>>();
        foreach (var modelType in DiscoverModels())
        {
            var genViewType = typeof(Koan.Canon.Model.ProjectionView<,>).MakeGenericType(modelType, typeof(object));
            var dataType = typeof(Koan.Data.Core.Data<,>).MakeGenericType(genViewType, typeof(string));
            Task task;
            if (!string.IsNullOrWhiteSpace(q))
            {
                var qM = dataType.GetMethod("Query", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(string), typeof(CancellationToken) })!;
                task = (Task)qM.Invoke(null, new object?[] { q!, set, ct })!;
            }
            else
            {
                var allM = dataType.GetMethod("All", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(CancellationToken) })!;
                task = (Task)allM.Invoke(null, new object?[] { set, ct })!;
            }
            await task.ConfigureAwait(false);
            var result = (System.Collections.IEnumerable?)GetTaskResult(task);
            if (result is null) continue;
            foreach (var it in result)
            {
                var doc = new Koan.Canon.Model.ProjectionView<object>
                {
                    Id = (string)genViewType.GetProperty("Id")!.GetValue(it)!,
                    ReferenceId = (string?)(genViewType.GetProperty("ReferenceId")?.GetValue(it)),
                    ViewName = (string)genViewType.GetProperty("ViewName")!.GetValue(it)!,
                    View = (object?)genViewType.GetProperty("View")!.GetValue(it)
                };
                // Only include matching view names if caller used a different route alias
                if (string.Equals(doc.ViewName, viewName, StringComparison.OrdinalIgnoreCase))
                    acc.Add(doc);
            }
        }
        return acc;
    }
}




