using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Sora.Web.Swagger;

public sealed class SoraHeadersOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // Ensure responses collection exists and has at least a 200 entry to attach headers to
        operation.Responses ??= new OpenApiResponses();
        if (operation.Responses.Count == 0)
        {
            operation.Responses["200"] = new OpenApiResponse { Description = "OK" };
        }

        foreach (var resp in operation.Responses.Values)
        {
            resp.Headers ??= new Dictionary<string, OpenApiHeader>(StringComparer.OrdinalIgnoreCase);
                if (!resp.Headers.ContainsKey(Sora.Web.Infrastructure.SoraWebConstants.Headers.SoraTraceId))
            {
                    resp.Headers.Add(Sora.Web.Infrastructure.SoraWebConstants.Headers.SoraTraceId, new OpenApiHeader
                {
                    Description = "Trace correlation id for this response.",
                    Schema = new OpenApiSchema { Type = "string" }
                });
            }
            // Heuristic: add paging header to GET operations
            // Developers can refine via custom attributes later.
            if (!resp.Headers.ContainsKey("Sora-InMemory-Paging") && context.ApiDescription.HttpMethod?.ToUpperInvariant() == "GET")
            {
                resp.Headers.Add("Sora-InMemory-Paging", new OpenApiHeader
                {
                    Description = "true when in-memory pagination fallback occurred.",
                    Schema = new OpenApiSchema { Type = "boolean" }
                });
            }
        }
    }
}
