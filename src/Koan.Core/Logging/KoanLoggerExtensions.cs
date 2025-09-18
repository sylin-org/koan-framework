using Microsoft.Extensions.Logging;

namespace Koan.Core.Logging;

public static class KoanLoggerExtensions
{
    public static void LogKoanInit(this ILogger logger, string message)
        => logger.LogInformation("[Koan:init] {Message}", message);

    public static void LogKoanDiscover(this ILogger logger, string providerType, string connectionString, bool success)
    {
        var status = success ? "OK" : "FAIL";
        logger.LogInformation("[Koan:discover] {ProviderType}: {ConnectionString} {Status}", 
            providerType, connectionString, status);
    }

    public static void LogKoanModules(this ILogger logger, string message)
        => logger.LogInformation("[Koan:modules] {Message}", message);

    public static void LogKoanServices(this ILogger logger, string message)
        => logger.LogInformation("[Koan:services] {Message}", message);

    public static void LogKoanHttp(this ILogger logger, string message)
        => logger.LogInformation("[Koan:http] {Message}", message);

    public static void LogKoanReady(this ILogger logger, string message)
        => logger.LogInformation("[Koan:ready] {Message}", message);

    public static void LogFlowWorker(this ILogger logger, LogLevel level, string message)
        => logger.Log(level, "[flow:worker] {Message}", message);

    public static void LogFlowWorkerInfo(this ILogger logger, string message)
        => logger.LogInformation("[flow:worker] {Message}", message);

    public static void LogFlowWorkerDebug(this ILogger logger, string message)
        => logger.LogDebug("[flow:worker] {Message}", message);

    public static void LogFlowWorkerWarning(this ILogger logger, string message)
        => logger.LogWarning("[flow:worker] {Message}", message);

    public static void LogFlowWorkerError(this ILogger logger, string message)
        => logger.LogError("[flow:worker] {Message}", message);

    public static void LogStartupPhase(this ILogger logger)
        => logger.LogInformation("├─ STARTUP ────────────────────────────────────────────────────────────────");

    public static void LogRuntimePhase(this ILogger logger)
        => logger.LogInformation("├─ RUNTIME ───────────────────────────────────────────────────────────────");

    public static void LogFrameworkHeader(this ILogger logger, string frameworkVersion, Dictionary<string, string> moduleVersions)
        => KoanLogger.LogFrameworkHeader(logger, frameworkVersion, moduleVersions);
}