using System;
using System.Collections;
using Microsoft.AspNetCore.Http;

namespace Koan.Web.Transformers;

/// <summary>
/// Non-generic boxing layer for <see cref="IEntityEnricher{TEntity}"/>. Lets the registry and the
/// result filter operate on enrichers without threading generic type parameters through every call site.
/// </summary>
internal interface IEntityEnricherInvoker
{
    Type EntityType { get; }

    bool ShouldActivate(HttpContext context);

    Task<object> Enrich(object model, HttpContext context);

    Task<object> EnrichMany(IEnumerable models, HttpContext context);
}
