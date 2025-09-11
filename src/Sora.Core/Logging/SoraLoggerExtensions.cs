using Microsoft.Extensions.Logging;

namespace Sora.Core.Logging;

public static class SoraLoggerExtensions
{
    public static void LogSoraInit(this ILogger logger, string message)
        => logger.LogInformation("[sora:init] {Message}", message);

    public static void LogSoraDiscover(this ILogger logger, string providerType, string connectionString, bool success)
    {
        var status = success ? "✓" : "✗";
        logger.LogInformation("[sora:discover] {ProviderType}: {ConnectionString} {Status}", 
            providerType, connectionString, status);
    }

    public static void LogSoraModules(this ILogger logger, string message)
        => logger.LogInformation("[sora:modules] {Message}", message);

    public static void LogSoraServices(this ILogger logger, string message)
        => logger.LogInformation("[sora:services] {Message}", message);

    public static void LogSoraHttp(this ILogger logger, string message)
        => logger.LogInformation("[sora:http] {Message}", message);

    public static void LogSoraReady(this ILogger logger, string message)
        => logger.LogInformation("[sora:ready] {Message}", message);

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
        => SoraLogger.LogFrameworkHeader(logger, frameworkVersion, moduleVersions);
}