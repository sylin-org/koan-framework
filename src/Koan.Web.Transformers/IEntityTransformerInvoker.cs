using System;
using System.Collections;
using System.IO;
using Microsoft.AspNetCore.Http;

namespace Koan.Web.Transformers;

internal interface IEntityTransformerInvoker
{
    Type EntityType { get; }

    Task<object?> ParseAsync(Stream body, string contentType, HttpContext httpContext);

    Task<object> ParseManyAsync(Stream body, string contentType, HttpContext httpContext);

    Task<object> TransformAsync(object model, HttpContext httpContext);

    Task<object> TransformManyAsync(IEnumerable models, HttpContext httpContext);
}
