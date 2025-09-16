using Microsoft.AspNetCore.Http;

namespace Koan.Web.GraphQl.Execution;

public interface IGraphQlExecutor
{
    Task<object> Execute(string query, object? variables, string? operationName, HttpContext http, CancellationToken ct);
    Task<string> GetSdl(CancellationToken ct);
}
