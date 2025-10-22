using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jint;
using Jint.Runtime;
using Koan.Mcp.CodeExecution;
using Koan.Mcp.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Mcp.CodeMode.Execution;

/// <summary>
/// JavaScript code executor using Jint engine with sandboxing.
/// Provides synchronous SDK interface to simplify LLM-generated code.
/// </summary>
/// <summary>
/// Jint-based implementation of unified CodeExecution.ICodeExecutor.
/// Bridges sandbox execution to the higher-level CodeExecutionResult contract.
/// </summary>
public sealed class JintCodeExecutor : Koan.Mcp.CodeExecution.ICodeExecutor
{
    private readonly IOptions<SandboxOptions> _options;
    private readonly ILogger<JintCodeExecutor> _logger;
    private readonly IServiceProvider _services;
    private readonly IOptions<CodeModeOptions> _codeModeOptions;

    public JintCodeExecutor(
        IOptions<SandboxOptions> options,
        ILogger<JintCodeExecutor> logger,
        IServiceProvider services,
        IOptions<CodeModeOptions> codeModeOptions)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _codeModeOptions = codeModeOptions ?? throw new ArgumentNullException(nameof(codeModeOptions));
    }

    public async Task<CodeExecutionResult> ExecuteAsync(CodeExecutionRequest request, CancellationToken cancellationToken = default)
    {
        var code = request.Source;
        if (string.IsNullOrWhiteSpace(code))
            return Failure(CodeModeErrorCodes.InvalidCode, "Code cannot be empty", request, startedUtc: DateTime.UtcNow, sdkCalls:0, cpuMs:0, scriptLength:0);

        var opts = _options.Value;

        // Validate code length
        if (code.Length > opts.MaxCodeLength)
            return Failure(CodeModeErrorCodes.CodeTooLong, $"Code length ({code.Length}) exceeds maximum ({opts.MaxCodeLength})", request, startedUtc: DateTime.UtcNow, sdkCalls:0, cpuMs:0, scriptLength: code.Length);

        var started = DateTime.UtcNow;
        var cmOpts = _codeModeOptions.Value;

        if (cmOpts.EnableAuditLogging)
        {
            _logger.LogInformation("CodeExec:start len={Len} entry={Entry} set={Set} corr={Correlation}", code.Length, request.EntryFunction ?? "(auto)", request.Set ?? "(none)", request.CorrelationId ?? "(none)");
        }
    var sw = Stopwatch.StartNew();
    // Create an execution scope so scoped services (e.g., EndpointToolExecutor, Db contexts) can be resolved safely
    using var scope = _services.CreateScope();
    var scopedProvider = scope.ServiceProvider;
    var bindings = scopedProvider.GetRequiredService<Sdk.KoanSdkBindings>();

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

                // If an entry function is specified, invoke it (supports default export pattern run())
                if (!string.IsNullOrWhiteSpace(request.EntryFunction))
                {
                    var fn = engine.GetValue(request.EntryFunction);
                    if (fn.IsUndefined())
                        throw new JavaScriptException($"Entry function '{request.EntryFunction}' not found");
                    engine.Invoke(request.EntryFunction);
                }
                else
                {
                    // Conventional: call global run() if present
                    var runFn = engine.GetValue("run");
                    if (!runFn.IsUndefined())
                        engine.Invoke("run");
                }

            }, cancellationToken).ConfigureAwait(false);

            sw.Stop();

            // Capture results from bindings
            var output = bindings.Out.GetAnswer();
            var structuredLogs = bindings.Out.GetLogs();
            var logs = structuredLogs.Select(l => $"{l.Level}:{l.Message}").ToArray();
            var entityCalls = bindings.Metrics.GetTotalCalls();

            // Quota enforcement post-execution
            if (cmOpts.MaxSdkCalls > 0 && entityCalls > cmOpts.MaxSdkCalls)
            {
                return Failure(CodeModeErrorCodes.SdkCallsExceeded, $"SDK call count {entityCalls} exceeded limit {cmOpts.MaxSdkCalls}", request, startedUtc: started, sdkCalls: entityCalls, cpuMs: (int)Math.Round(sw.Elapsed.TotalMilliseconds), scriptLength: code.Length);
            }

            if (cmOpts.RequireAnswer && string.IsNullOrWhiteSpace(output))
            {
                return Failure(CodeModeErrorCodes.MissingAnswer, "Execution completed but no answer was produced (SDK.Out.answer not called)", request, startedUtc: started, sdkCalls: entityCalls, cpuMs: (int)Math.Round(sw.Elapsed.TotalMilliseconds), scriptLength: code.Length);
            }

            if (cmOpts.MaxLogEntries > 0 && logs.Length > cmOpts.MaxLogEntries)
            {
                logs = logs.Take(cmOpts.MaxLogEntries).Concat(new[] { "info:log entries truncated" }).ToArray();
            }

            _logger.LogDebug(
                "Code execution succeeded in {Ms}ms with {Calls} entity calls",
                sw.Elapsed.TotalMilliseconds,
                entityCalls);

            var successResult = new CodeExecutionResult(
                Success: true,
                Result: null,
                TextResponse: output,
                Logs: logs,
                Diagnostics: new CodeExecutionDiagnostics(
                    SdkCalls: entityCalls,
                    CpuMs: (int)Math.Round(sw.Elapsed.TotalMilliseconds),
                    MemoryBytes: 0,
                    ScriptLength: code.Length,
                    StartedUtc: started,
                    CompletedUtc: DateTime.UtcNow
                ),
                Error: null
            );
            if (cmOpts.EnableAuditLogging)
            {
                _logger.LogInformation("CodeExec:success sdkCalls={Calls} cpuMs={Cpu} answerLen={AnswerLen}", entityCalls, successResult.Diagnostics.CpuMs, output?.Length ?? 0);
            }
            return successResult;
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

            var fail = Failure(CodeModeErrorCodes.JavaScriptError, ex.Message, request, started, sdkCalls: bindings.Metrics.GetTotalCalls(), cpuMs: (int)Math.Round(sw.Elapsed.TotalMilliseconds), scriptLength: code.Length, stack: ex.StackTrace);
            if (cmOpts.EnableAuditLogging)
            {
                _logger.LogInformation("CodeExec:fail type=js line={Line} col={Col} msg={Msg}", ex.Location.Start.Line, ex.Location.Start.Column, ex.Message);
            }
            return fail;
        }
        catch (ExecutionCanceledException)
        {
            sw.Stop();

            _logger.LogWarning(
                "Code execution timeout after {Ms}ms (limit: {Limit}ms)",
                sw.Elapsed.TotalMilliseconds,
                opts.CpuMilliseconds);

            var fail = Failure(CodeModeErrorCodes.Timeout, $"Execution exceeded {opts.CpuMilliseconds}ms time limit", request, started, sdkCalls: bindings.Metrics.GetTotalCalls(), cpuMs: (int)Math.Round(sw.Elapsed.TotalMilliseconds), scriptLength: code.Length);
            if (cmOpts.EnableAuditLogging)
            {
                _logger.LogInformation("CodeExec:fail type=timeout cpuMs={Cpu}", sw.Elapsed.TotalMilliseconds);
            }
            return fail;
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

            var fail = Failure(CodeModeErrorCodes.ExecutionError, $"Unexpected error: {ex.Message}", request, started, sdkCalls: bindings.Metrics.GetTotalCalls(), cpuMs: (int)Math.Round(sw.Elapsed.TotalMilliseconds), scriptLength: code.Length, stack: ex.StackTrace);
            if (cmOpts.EnableAuditLogging)
            {
                _logger.LogInformation("CodeExec:fail type=unexpected msg={Msg}", ex.Message);
            }
            return fail;
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

    private CodeExecutionResult Failure(string code, string message, CodeExecutionRequest request, DateTime startedUtc, int sdkCalls, int cpuMs, int scriptLength, string? stack = null)
    {
        return new CodeExecutionResult(
            Success: false,
            Result: null,
            TextResponse: null,
            Logs: Array.Empty<string>(),
            Diagnostics: new CodeExecutionDiagnostics(
                SdkCalls: sdkCalls,
                CpuMs: cpuMs,
                MemoryBytes: 0,
                ScriptLength: scriptLength,
                StartedUtc: startedUtc,
                CompletedUtc: DateTime.UtcNow
            ),
            Error: new CodeExecutionError(code, message, stack)
        );
    }
}
