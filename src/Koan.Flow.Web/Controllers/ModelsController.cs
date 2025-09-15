using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Koan.Flow.Infrastructure;
using Koan.Flow.Model;
using Koan.Data.Core;
using System.Reflection;

namespace Koan.Flow.Web.Controllers;

[ApiController]
[Route("models/{model}/views")] // /models/{model}/views/{view}[/{referenceId}]
public sealed class ModelsController : ControllerBase
{
    private readonly ILogger<ModelsController> _logger;
    public ModelsController(ILogger<ModelsController> logger) => _logger = logger;

    [HttpGet("{view}/{referenceId}")]
    public async Task<IActionResult> GetOne([FromRoute] string model, [FromRoute] string view, [FromRoute] string referenceId, CancellationToken ct)
    {
        var modelType = Flow.Infrastructure.FlowRegistry.ResolveModel(model);
        if (modelType is null) return NotFound(new { error = $"Unknown model '{model}'" });

        var set = FlowSets.View(modelType, view);
        var entityType = ResolveEntityType(modelType, view);
        var queryMethod = entityType.GetMethod("Query", BindingFlags.Public | BindingFlags.Static, new Type[] { typeof(string), typeof(string), typeof(CancellationToken) });
        if (queryMethod is null) return Problem("View entity doesn't expose Query");

        var expr = $"ReferenceId == '{referenceId}'";
        var task = (Task)queryMethod.Invoke(null, new object?[] { expr, set, ct })!;
        await task.ConfigureAwait(false);
        var result = GetTaskResult(task);
        var first = (result as System.Collections.IEnumerable)?.Cast<object?>().FirstOrDefault();
        _logger.LogInformation("ModelsController.GetOne model={Model} view={View} ref={Ref} found={Found}", model, view, referenceId, first is not null);
        return first is null ? NotFound() : Ok(first);
    }

    [HttpGet("{view}")]
    public async Task<IActionResult> GetPage([FromRoute] string model, [FromRoute] string view, [FromQuery] string? q, [FromQuery] int? page = 1, [FromQuery] int? size = 50, CancellationToken ct = default)
    {
        var modelType = Flow.Infrastructure.FlowRegistry.ResolveModel(model);
        if (modelType is null) return NotFound(new { error = $"Unknown model '{model}'" });

        var set = FlowSets.View(modelType, view);
        var entityType = ResolveEntityType(modelType, view);

        var p = Math.Max(1, page ?? 1);
        var s = Math.Clamp(size ?? 50, 1, 500);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var queryMethod = entityType.GetMethod("Query", BindingFlags.Public | BindingFlags.Static, new Type[] { typeof(string), typeof(string), typeof(CancellationToken) });
            if (queryMethod is null) return Problem("View entity doesn't expose Query");
            var task = (Task)queryMethod.Invoke(null, new object?[] { q, set, ct })!;
            await task.ConfigureAwait(false);
            var result = (GetTaskResult(task) as System.Collections.IList)!;
            var total = result.Count;
            var skip = (p - 1) * s;
            var pageItems = result.Cast<object?>().Skip(skip).Take(s).ToList();
            var hasNext = skip + pageItems.Count < total;
            _logger.LogInformation("ModelsController.GetPage(q) model={Model} view={View} total={Total} page={Page} size={Size} returned={Returned}", model, view, total, p, s, pageItems.Count);
            return Ok(new { page = p, size = s, total, hasNext, items = pageItems });
        }

        using (DataSetContext.With(set))
        {
            var pageMethod = entityType.GetMethod("Page", BindingFlags.Public | BindingFlags.Static, new Type[] { typeof(int), typeof(int), typeof(CancellationToken) });
            if (pageMethod is null) return Problem("View entity doesn't expose Page");
            var task = (Task)pageMethod.Invoke(null, new object?[] { p, s, ct })!;
            await task.ConfigureAwait(false);
            var items = (GetTaskResult(task) as System.Collections.IList)!;
            var hasNext = items.Count == s;
            _logger.LogInformation("ModelsController.GetPage model={Model} view={View} page={Page} size={Size} returned={Returned}", model, view, p, s, items.Count);
            return Ok(new { page = p, size = s, hasNext, items });
        }
    }

    private static Type ResolveEntityType(Type modelType, string view)
    {
        if (string.Equals(view, Constants.Views.Canonical, StringComparison.OrdinalIgnoreCase))
            return typeof(CanonicalProjection<>).MakeGenericType(modelType);
        if (string.Equals(view, Constants.Views.Lineage, StringComparison.OrdinalIgnoreCase))
            return typeof(LineageProjection<>).MakeGenericType(modelType);
        return typeof(ProjectionView<,>).MakeGenericType(modelType, typeof(object));
    }

    private static object? GetTaskResult(Task t)
    {
        var type = t.GetType();
        if (type.IsGenericType)
        {
            return type.GetProperty("Result")!.GetValue(t);
        }
        return null;
    }
}
