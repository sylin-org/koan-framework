using HotChocolate.Execution;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sora.Web.GraphQl.Infrastructure;

namespace Sora.Web.GraphQl.Execution;

internal sealed class GraphQlExecutor : IGraphQlExecutor
{
    private readonly IRequestExecutorResolver _executors;
    private readonly IConfiguration _cfg;
    private readonly ILogger<GraphQlExecutor> _log;

    public GraphQlExecutor(IRequestExecutorResolver executors, IConfiguration cfg, ILogger<GraphQlExecutor> log)
    {
        _executors = executors; _cfg = cfg; _log = log;
    }

    public async Task<object> Execute(string query, object? variables, string? operationName, HttpContext http, CancellationToken ct)
    {
        var executor = await _executors.GetRequestExecutorAsync();
        var varsDict = VariableNormalizer.ToDict(variables);
        var debug = DebugToggle.IsEnabled(http, _cfg);
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
