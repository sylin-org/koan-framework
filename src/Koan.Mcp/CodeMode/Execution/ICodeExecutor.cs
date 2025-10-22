using System.Threading;
using System.Threading.Tasks;

namespace Koan.Mcp.CodeMode.Execution;

/// <summary>
/// Abstraction for JavaScript runtime engines.
/// Allows future swap to ClearScript or other engines without changing SDK surface.
/// </summary>
public interface ICodeExecutor
{
    /// <summary>
    /// Execute JavaScript code in a sandboxed environment.
    /// Code has access to SDK bindings provided via context parameter.
    /// </summary>
    /// <param name="code">JavaScript code to execute</param>
    /// <param name="bindings">SDK bindings exposed to the code</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Execution result with output, logs, and metrics</returns>
    Task<ExecutionResult> ExecuteAsync(
        string code,
        Sdk.KoanSdkBindings bindings,
        CancellationToken cancellationToken);

    /// <summary>
    /// Validate code syntax without executing.
    /// </summary>
    /// <param name="code">JavaScript code to validate</param>
    /// <param name="error">Error message if validation fails</param>
    /// <returns>True if syntax is valid, false otherwise</returns>
    bool ValidateSyntax(string code, out string? error);
}
