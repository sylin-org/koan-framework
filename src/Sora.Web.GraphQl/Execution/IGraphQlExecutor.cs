using Microsoft.AspNetCore.Http;

namespace Sora.Web.GraphQl.Execution;

public interface IGraphQlExecutor
{
    Task<object> Execute(string query, object? variables, string? operationName, HttpContext http, CancellationToken ct);
    Task<string> GetSdl(CancellationToken ct);
}
