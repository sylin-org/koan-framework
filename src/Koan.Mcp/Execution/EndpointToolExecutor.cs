using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Web.Endpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using JsonException = Newtonsoft.Json.JsonException;

namespace Koan.Mcp.Execution;

public sealed class EndpointToolExecutor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly McpEntityRegistry _registry;
    private readonly RequestTranslator _requestTranslator;
    private readonly ResponseTranslator _responseTranslator;
    private readonly ILogger<EndpointToolExecutor> _logger;

    public EndpointToolExecutor(
        IServiceScopeFactory scopeFactory,
        McpEntityRegistry registry,
        RequestTranslator requestTranslator,
        ResponseTranslator responseTranslator,
        ILogger<EndpointToolExecutor> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _requestTranslator = requestTranslator ?? throw new ArgumentNullException(nameof(requestTranslator));
        _responseTranslator = responseTranslator ?? throw new ArgumentNullException(nameof(responseTranslator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<McpToolExecutionResult> ExecuteAsync(string toolName, JObject? arguments, CancellationToken cancellationToken)
    {
        if (!_registry.TryGetTool(toolName, out var registration, out var tool))
        {
            return McpToolExecutionResult.Failure(CodeMode.Execution.CodeModeErrorCodes.ToolNotFound, $"Tool '{toolName}' is not registered.");
        }

        using var scope = _scopeFactory.CreateScope();
        var provider = scope.ServiceProvider;
        var serviceType = typeof(IEntityEndpointService<,>).MakeGenericType(registration.EntityType, registration.KeyType);
        var service = provider.GetService(serviceType);
        if (service is null)
        {
            _logger.LogError(
                "IEntityEndpointService<{Entity},{Key}> is not registered in the current scope.",
                registration.EntityType.Name,
                registration.KeyType.Name);
            return McpToolExecutionResult.Failure(
                CodeMode.Execution.CodeModeErrorCodes.ServiceUnavailable,
                $"Entity endpoint service for '{registration.DisplayName}' is not available.");
        }

        try
        {
            var translation = _requestTranslator.Translate(provider, registration, tool, arguments, cancellationToken);
            var endpointResult = await InvokeServiceAsync(service, translation);
            return _responseTranslator.Translate(registration, tool, endpointResult);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON payload for tool {Tool}.", toolName);
            var diagnostics = new JObject
            {
                ["tool"] = toolName,
                ["reason"] = "invalid_payload"
            };
            return McpToolExecutionResult.Failure(CodeMode.Execution.CodeModeErrorCodes.InvalidPayload, ex.Message, diagnostics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error while executing MCP tool {Tool}.", toolName);
            var diagnostics = new JObject
            {
                ["tool"] = toolName,
                ["reason"] = "execution_error"
            };
            return McpToolExecutionResult.Failure(CodeMode.Execution.CodeModeErrorCodes.ExecutionError, ex.Message, diagnostics);
        }
    }

    private static async Task<EntityEndpointResult> InvokeServiceAsync(object service, RequestTranslation translation)
    {
        if (service is null) throw new ArgumentNullException(nameof(service));
        if (translation is null) throw new ArgumentNullException(nameof(translation));

        var method = service.GetType().GetMethod(translation.MethodName);
        if (method is null)
        {
            throw new InvalidOperationException($"Service '{service.GetType().FullName}' does not implement {translation.MethodName}.");
        }

        if (method.Invoke(service, new[] { translation.Request }) is not Task task)
        {
            throw new InvalidOperationException($"Invocation of {translation.MethodName} did not return a Task instance.");
        }

        await task;
        var resultProperty = task.GetType().GetProperty("Result");
        if (resultProperty?.GetValue(task) is not EntityEndpointResult result)
        {
            throw new InvalidOperationException($"Invocation of {translation.MethodName} did not yield an EntityEndpointResult.");
        }

        return result;
    }
}
