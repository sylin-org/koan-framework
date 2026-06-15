using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Koan.Mcp.CustomTools;

/// <summary>
/// Binds a custom tool's parameters (call arguments + injected <see cref="IServiceProvider"/> /
/// <see cref="CancellationToken"/>), invokes the static method, awaits a <see cref="Task"/>/
/// <see cref="ValueTask"/> result, and serializes the return value to a JSON token.
/// </summary>
public sealed class McpCustomToolInvoker
{
    public async Task<JToken> Invoke(McpCustomTool tool, JObject? arguments, IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tool);
        ArgumentNullException.ThrowIfNull(services);

        var args = new object?[tool.Parameters.Count];
        for (var i = 0; i < tool.Parameters.Count; i++)
        {
            var parameter = tool.Parameters[i];
            args[i] = parameter.Source switch
            {
                McpCustomToolParameterSource.CancellationToken => cancellationToken,
                McpCustomToolParameterSource.ServiceProvider => services,
                _ => BindArgument(parameter, arguments)
            };
        }

        object? result;
        try
        {
            result = tool.Method.Invoke(null, args);
        }
        catch (TargetInvocationException tie) when (tie.InnerException is not null)
        {
            // Surface the real exception (the RPC layer maps it to a JSON-RPC error).
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(tie.InnerException);
            throw; // unreachable
        }

        switch (result)
        {
            case Task task:
                await task.ConfigureAwait(false);
                result = GetTaskResult(task);
                break;
            case ValueTask valueTask:
                await valueTask.ConfigureAwait(false);
                result = null;
                break;
        }

        return result is null ? JValue.CreateNull() : JToken.FromObject(result);
    }

    private static object? BindArgument(McpCustomToolParameter parameter, JObject? arguments)
    {
        if (arguments is not null
            && arguments.TryGetValue(parameter.Name, StringComparison.OrdinalIgnoreCase, out var node)
            && node.Type != JTokenType.Null)
        {
            try
            {
                return node.ToObject(parameter.Type);
            }
            catch
            {
                // Fall through to default/empty when the supplied value cannot bind to the target type.
            }
        }

        if (parameter.DefaultValue is not null)
        {
            return parameter.DefaultValue;
        }

        return parameter.Type.IsValueType && Nullable.GetUnderlyingType(parameter.Type) is null
            ? Activator.CreateInstance(parameter.Type)
            : null;
    }

    private static object? GetTaskResult(Task task)
    {
        var type = task.GetType();
        if (!type.IsGenericType) return null; // non-generic Task → void

        var value = type.GetProperty("Result")?.GetValue(task);
        // Task (non-generic) surfaces as Task<VoidTaskResult> internally — treat as void.
        return value is not null && value.GetType().Name == "VoidTaskResult" ? null : value;
    }
}
