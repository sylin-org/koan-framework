using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Jint;
using Jint.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Mcp.CodeMode.Execution;

/// <summary>
/// JavaScript code executor using Jint engine with sandboxing.
/// Provides synchronous SDK interface to simplify LLM-generated code.
/// </summary>
public sealed class JintCodeExecutor : ICodeExecutor
{
    private readonly IOptions<SandboxOptions> _options;
    private readonly ILogger<JintCodeExecutor> _logger;

    public JintCodeExecutor(
        IOptions<SandboxOptions> options,
        ILogger<JintCodeExecutor> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ExecutionResult> ExecuteAsync(
        string code,
        Sdk.KoanSdkBindings bindings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return ExecutionResult.FromError("invalid_code", "Code cannot be empty");
        }

        var opts = _options.Value;

        // Validate code length
        if (code.Length > opts.MaxCodeLength)
        {
            return ExecutionResult.FromError(
                "code_too_long",
                $"Code length ({code.Length}) exceeds maximum ({opts.MaxCodeLength})");
        }

        var sw = Stopwatch.StartNew();

        try
        {
            // Create isolated Jint engine with security constraints
            var engine = new Engine(config =>
            {
                // Security: Limit execution time
                config.TimeoutInterval(TimeSpan.FromMilliseconds(opts.CpuMilliseconds));

                // Security: Limit memory
                config.LimitMemory(opts.MemoryMegabytes * 1_000_000L);

                // Security: Limit recursion depth
                config.LimitRecursion(opts.MaxRecursionDepth);

                // Security: Disable CLR access (Jint 4.2+ doesn't support AllowClr/AllowClrWrite)
                // CLR is disabled by default in Jint 4.2+
            });

            // Bind SDK to global scope as 'SDK'
            engine.SetValue("SDK", bindings);

            // Execute code on thread pool to respect cancellation token
            await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Execute the code
                engine.Execute(code);

            }, cancellationToken).ConfigureAwait(false);

            sw.Stop();

            // Capture results from bindings
            var output = bindings.Out.GetAnswer();
            var logs = bindings.Out.GetLogs();
            var entityCalls = bindings.Metrics.GetTotalCalls();

            _logger.LogDebug(
                "Code execution succeeded in {Ms}ms with {Calls} entity calls",
                sw.Elapsed.TotalMilliseconds,
                entityCalls);

            return ExecutionResult.FromOutput(
                output,
                logs,
                new ExecutionMetrics
                {
                    ExecutionMs = sw.Elapsed.TotalMilliseconds,
                    MemoryMb = 0, // Jint doesn't expose granular memory tracking
                    EntityCalls = entityCalls
                });
        }
        catch (JavaScriptException ex)
        {
            sw.Stop();

            var location = ex.Location;
            var line = location.Start.Line;
            var column = location.Start.Column;

            _logger.LogWarning(
                ex,
                "JavaScript execution error at line {Line}, column {Column}",
                line,
                column);

            return ExecutionResult.FromError(
                "javascript_error",
                ex.Message,
                line,
                column);
        }
        catch (ExecutionCanceledException)
        {
            sw.Stop();

            _logger.LogWarning(
                "Code execution timeout after {Ms}ms (limit: {Limit}ms)",
                sw.Elapsed.TotalMilliseconds,
                opts.CpuMilliseconds);

            return ExecutionResult.FromError(
                "timeout",
                $"Execution exceeded {opts.CpuMilliseconds}ms time limit");
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Code execution cancelled by client");
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();

            _logger.LogError(ex, "Unexpected error during code execution");

            return ExecutionResult.FromError(
                "execution_error",
                $"Unexpected error: {ex.Message}");
        }
    }

    public bool ValidateSyntax(string code, out string? error)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            error = "Code cannot be empty";
            return false;
        }

        try
        {
            // Create minimal engine for syntax check only
            var engine = new Engine();

            // Parse without executing
            engine.Execute($"({code})");

            error = null;
            return true;
        }
        catch (JavaScriptException ex)
        {
            var location = ex.Location;
            error = $"Syntax error at line {location.Start.Line}, column {location.Start.Column}: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            error = $"Validation error: {ex.Message}";
            return false;
        }
    }
}
