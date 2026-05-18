using System;
using System.Collections;
using System.IO;
using Microsoft.AspNetCore.Http;

namespace Koan.Web.Transformers;

internal interface IEntityTransformerInvoker
{
    Type EntityType { get; }

    bool ShouldActivate(HttpContext context);

    Task<object?> Parse(Stream body, string contentType, HttpContext httpContext);

    Task<object> ParseMany(Stream body, string contentType, HttpContext httpContext);

    Task<object> Transform(object model, HttpContext httpContext);

    Task<object> TransformMany(IEnumerable models, HttpContext httpContext);
}
