using Microsoft.Extensions.Logging;

namespace Sora.Core.Logging;

public static class SoraLogger
{
    private static readonly object _lock = new();
    private static bool _headerPrinted = false;
    
    public static void LogFrameworkHeader(ILogger logger, string frameworkVersion, Dictionary<string, string> moduleVersions)
    {
        lock (_lock)
        {
            if (_headerPrinted) return;
            _headerPrinted = true;
        }

        logger.LogInformation("┌─ SORA FRAMEWORK v{FrameworkVersion} ──────────────────────────────────────────────────", frameworkVersion);
        logger.LogInformation("│ Core: {CoreVersion}", frameworkVersion);
        
        foreach (var module in moduleVersions.OrderBy(kvp => kvp.Key))
        {
            logger.LogInformation("│   ├─ {ModuleName}: {Version}", module.Key, module.Value);
        }
        
        if (moduleVersions.Any())
        {
            var lastModule = moduleVersions.OrderBy(kvp => kvp.Key).Last();
            logger.LogInformation("│   └─ {ModuleName}: {Version}", lastModule.Key, lastModule.Value);
        }
    }

    public static void LogStartupPhase(ILogger logger)
    {
        logger.LogInformation("├─ STARTUP ────────────────────────────────────────────────────────────────");
    }

    public static void LogRuntimePhase(ILogger logger)
    {
        logger.LogInformation("├─ RUNTIME ───────────────────────────────────────────────────────────────");
    }

    public static void LogSoraEvent(ILogger logger, LogLevel level, string context, string message)
    {
        var levelChar = GetLogLevelChar(level);
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var paddedContext = context.PadRight(15); // Consistent context column width
        logger.Log(level, "│ {Level} {Timestamp} {Context} {Message}", levelChar, timestamp, paddedContext, message);
    }

    public static void LogSoraInit(ILogger logger, string message)
    {
        LogSoraEvent(logger, LogLevel.Information, "sora:init", message);
    }

    public static void LogSoraDiscover(ILogger logger, string providerType, string connectionString, bool success)
    {
        var status = success ? "✓" : "✗";
        LogSoraEvent(logger, LogLevel.Information, "sora:discover", $"{providerType}: {connectionString} {status}");
    }

    public static void LogSoraModules(ILogger logger, string message)
    {
        LogSoraEvent(logger, LogLevel.Information, "sora:modules", message);
    }

    public static void LogSoraServices(ILogger logger, string message)
    {
        LogSoraEvent(logger, LogLevel.Information, "sora:services", message);
    }

    public static void LogSoraHttp(ILogger logger, string message)
    {
        LogSoraEvent(logger, LogLevel.Information, "sora:http", message);
    }

    public static void LogSoraReady(ILogger logger, string message)
    {
        LogSoraEvent(logger, LogLevel.Information, "sora:ready", message);
    }

    public static void LogFlowWorker(ILogger logger, LogLevel level, string message)
    {
        LogSoraEvent(logger, level, "flow:worker", message);
    }

    private static string GetLogLevelChar(LogLevel level) => level switch
    {
        LogLevel.Error => "E",
        LogLevel.Warning => "W", 
        LogLevel.Information => "I",
        LogLevel.Debug => "D",
        LogLevel.Trace => "T",
        _ => "I"
    };
}