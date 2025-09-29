using Microsoft.AspNetCore.Mvc;
using Koan.Data.Core;
using Koan.Canon.Infrastructure;
using Koan.Canon.Model;
using Koan.Web.Controllers;
using System.Reflection;

namespace Koan.Canon.Web.Controllers;

/// <summary>
/// Generic controller for Canon materialized roots and model-scoped views.
/// Route is assigned via GenericControllers at startup: /api/Canon/{model}
/// </summary>
[ApiController]
public class CanonEntityController<TModel> : EntityController<DynamicCanonEntity<TModel>> where TModel : CanonEntity<TModel>, new()
{
    // Model-scoped convenience endpoints for Canonical and lineage views
    [HttpGet("views/Canonical/{referenceId}")]
    public async Task<IActionResult> GetCanonical([FromRoute] string referenceId, CancellationToken ct)
    {
        var set = CanonSets.ViewShort(Constants.Views.Canonical);
        var viewType = typeof(CanonicalProjection<>).MakeGenericType(typeof(TModel));
        var doc = await TryGetByIdAsync(viewType, $"{Constants.Views.Canonical}::{referenceId}", set, ct).ConfigureAwait(false);
        return doc is null ? NotFound() : Ok(doc);
    }

    [HttpGet("views/lineage/{referenceId}")]
    public async Task<IActionResult> GetLineage([FromRoute] string referenceId, CancellationToken ct)
    {
        var set = CanonSets.ViewShort(Constants.Views.Lineage);
        var viewType = typeof(LineageProjection<>).MakeGenericType(typeof(TModel));
        var doc = await TryGetByIdAsync(viewType, $"{Constants.Views.Lineage}::{referenceId}", set, ct).ConfigureAwait(false);
        return doc is null ? NotFound() : Ok(doc);
    }

    // Helper: provider-neutral fetch by id within a set using Data<TEntity, string>.GetAsync
    private static async Task<object?> TryGetByIdAsync(Type entityType, string id, string? set, CancellationToken ct)
    {
        var dataType = typeof(Data<,>).MakeGenericType(entityType, typeof(string));
        // Prefer scoped set when provided
        if (!string.IsNullOrWhiteSpace(set))
        {
            using (DataSetContext.With(set))
            {
                var getM = dataType.GetMethod("GetAsync", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(string), typeof(CancellationToken) });
                if (getM is null) return null;
                var t = (Task)getM.Invoke(null, new object?[] { id, set, ct })!;
                await t.ConfigureAwait(false);
                return GetTaskResult(t);
            }
        }
        else
        {
            var getM = dataType.GetMethod("GetAsync", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string), typeof(CancellationToken) });
            if (getM is null) return null;
            var t = (Task)getM.Invoke(null, new object?[] { id, ct })!;
            await t.ConfigureAwait(false);
            return GetTaskResult(t);
        }
    }

    private static object? GetTaskResult(Task t)
    {
        var type = t.GetType();
        if (type.IsGenericType) return type.GetProperty("Result")!.GetValue(t);
        return null;
    }
}




