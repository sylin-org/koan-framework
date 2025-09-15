using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Koan.Web.GraphQl.Infrastructure;

namespace Koan.Web.GraphQl.Execution;

internal sealed class GraphQlExecutor : IGraphQlExecutor
{
    private readonly IRequestExecutorResolver _executors;
    private readonly ILogger<GraphQlExecutor> _log;

    public GraphQlExecutor(IRequestExecutorResolver executors, ILogger<GraphQlExecutor> log)
    {
        _executors = executors; _log = log;
    }

    public async Task<object> Execute(string query, object? variables, string? operationName, HttpContext http, CancellationToken ct)
    {
        var executor = await _executors.GetRequestExecutorAsync();
        var varsDict = VariableNormalizer.ToDict(variables);
        var debug = DebugToggle.IsEnabled(http);
        var rq = QueryRequestBuilder.New()
            .SetQuery(query)
            .SetVariableValues(varsDict)
            .SetOperation(operationName)
            .SetServices(http.RequestServices)
            .Create();
        var result = await executor.ExecuteAsync(rq, ct);
        return ResultComposer.Compose(result, http, varsDict, variables);
    }

    public async Task<string> GetSdl(CancellationToken ct)
    {
        var executor = await _executors.GetRequestExecutorAsync();
        return executor.Schema?.ToString() ?? string.Empty;
    }
}
