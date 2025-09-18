using Microsoft.Extensions.Logging;

namespace Koan.Core.Logging;

public static class KoanLogger
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

        logger.LogInformation("┌─ Koan FRAMEWORK v{FrameworkVersion} ──────────────────────────────────────────────────", frameworkVersion);
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

    public static void LogKoanEvent(ILogger logger, LogLevel level, string context, string message)
    {
        var levelChar = GetLogLevelChar(level);
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        var paddedContext = context.PadRight(15); // Consistent context column width
        logger.Log(level, "│ {Level} {Timestamp} {Context} {Message}", levelChar, timestamp, paddedContext, message);
    }

    public static void LogKoanInit(ILogger logger, string message)
    {
        LogKoanEvent(logger, LogLevel.Information, "Koan:init", message);
    }

    public static void LogKoanDiscover(ILogger logger, string providerType, string connectionString, bool success)
    {
        var status = success ? "OK" : "FAIL";
        LogKoanEvent(logger, LogLevel.Information, "Koan:discover", $"{providerType}: {connectionString} {status}");
    }

    public static void LogKoanModules(ILogger logger, string message)
    {
        LogKoanEvent(logger, LogLevel.Information, "Koan:modules", message);
    }

    public static void LogKoanServices(ILogger logger, string message)
    {
        LogKoanEvent(logger, LogLevel.Information, "Koan:services", message);
    }

    public static void LogKoanHttp(ILogger logger, string message)
    {
        LogKoanEvent(logger, LogLevel.Information, "Koan:http", message);
    }

    public static void LogKoanReady(ILogger logger, string message)
    {
        LogKoanEvent(logger, LogLevel.Information, "Koan:ready", message);
    }

    public static void LogFlowWorker(ILogger logger, LogLevel level, string message)
    {
        LogKoanEvent(logger, level, "flow:worker", message);
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