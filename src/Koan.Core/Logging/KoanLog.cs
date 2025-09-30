using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Koan.Core.Logging;

/// <summary>
/// Centralized stage-aware logging helpers for Koan.
/// Provides first-class entry points for each canonical stage.
/// </summary>
public static class KoanLog
{
    private static ILoggerFactory? _loggerFactory;

    internal static void AttachFactory(ILoggerFactory factory)
    {
        if (factory is null) throw new ArgumentNullException(nameof(factory));
        Interlocked.Exchange(ref _loggerFactory, factory);
    }

    internal static void DetachFactory(ILoggerFactory factory)
    {
        if (factory is null) return;
        Interlocked.CompareExchange(ref _loggerFactory, null, factory);
    }

    internal static ILogger? CreateLogger(string categoryName)
    {
        var factory = Volatile.Read(ref _loggerFactory);
        if (factory is null || string.IsNullOrWhiteSpace(categoryName))
        {
            return null;
        }

        try
        {
            return factory.CreateLogger(categoryName);
        }
        catch
        {
            return null;
        }
    }

    public static KoanLogScope For<T>() => new(GetCategoryName(typeof(T)));

    public static KoanLogScope For(Type type) => new(GetCategoryName(type));

    public static KoanLogScope For(string categoryName) => new(string.IsNullOrWhiteSpace(categoryName) ? "Koan" : categoryName);

    public static void StageDebug(ILogger? logger, KoanLogStage stage, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => Write(logger, stage, LogLevel.Debug, action, outcome, context);

    public static void StageInfo(ILogger? logger, KoanLogStage stage, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => Write(logger, stage, LogLevel.Information, action, outcome, context);

    public static void StageWarning(ILogger? logger, KoanLogStage stage, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => Write(logger, stage, LogLevel.Warning, action, outcome, context);

    public static void StageError(ILogger? logger, KoanLogStage stage, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => Write(logger, stage, LogLevel.Error, action, outcome, context);

    public static void BuildDebug(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageDebug(logger, KoanLogStage.Bldg, action, outcome, context);

    public static void BuildInfo(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageInfo(logger, KoanLogStage.Bldg, action, outcome, context);

    public static void BuildWarning(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageWarning(logger, KoanLogStage.Bldg, action, outcome, context);

    public static void BuildError(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageError(logger, KoanLogStage.Bldg, action, outcome, context);

    public static void BootDebug(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageDebug(logger, KoanLogStage.Boot, action, outcome, context);

    public static void BootInfo(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageInfo(logger, KoanLogStage.Boot, action, outcome, context);

    public static void BootWarning(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageWarning(logger, KoanLogStage.Boot, action, outcome, context);

    public static void BootError(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageError(logger, KoanLogStage.Boot, action, outcome, context);

    public static void ConfigDebug(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageDebug(logger, KoanLogStage.Cnfg, action, outcome, context);

    public static void ConfigInfo(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageInfo(logger, KoanLogStage.Cnfg, action, outcome, context);

    public static void ConfigWarning(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageWarning(logger, KoanLogStage.Cnfg, action, outcome, context);

    public static void ConfigError(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageError(logger, KoanLogStage.Cnfg, action, outcome, context);

    public static void SnapshotDebug(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageDebug(logger, KoanLogStage.Snap, action, outcome, context);

    public static void SnapshotInfo(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageInfo(logger, KoanLogStage.Snap, action, outcome, context);

    public static void SnapshotWarning(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageWarning(logger, KoanLogStage.Snap, action, outcome, context);

    public static void SnapshotError(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageError(logger, KoanLogStage.Snap, action, outcome, context);

    public static void DataDebug(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageDebug(logger, KoanLogStage.Data, action, outcome, context);

    public static void DataInfo(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageInfo(logger, KoanLogStage.Data, action, outcome, context);

    public static void DataWarning(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageWarning(logger, KoanLogStage.Data, action, outcome, context);

    public static void DataError(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageError(logger, KoanLogStage.Data, action, outcome, context);

    public static void ServiceDebug(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageDebug(logger, KoanLogStage.Srvc, action, outcome, context);

    public static void ServiceInfo(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageInfo(logger, KoanLogStage.Srvc, action, outcome, context);

    public static void ServiceWarning(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageWarning(logger, KoanLogStage.Srvc, action, outcome, context);

    public static void ServiceError(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageError(logger, KoanLogStage.Srvc, action, outcome, context);

    public static void HealthDebug(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageDebug(logger, KoanLogStage.Hlth, action, outcome, context);

    public static void HealthInfo(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageInfo(logger, KoanLogStage.Hlth, action, outcome, context);

    public static void HealthWarning(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageWarning(logger, KoanLogStage.Hlth, action, outcome, context);

    public static void HealthError(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageError(logger, KoanLogStage.Hlth, action, outcome, context);

    public static void HostDebug(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageDebug(logger, KoanLogStage.Host, action, outcome, context);

    public static void HostInfo(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageInfo(logger, KoanLogStage.Host, action, outcome, context);

    public static void HostWarning(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageWarning(logger, KoanLogStage.Host, action, outcome, context);

    public static void HostError(ILogger? logger, string action, string? outcome = null, params (string Key, object? Value)[] context)
        => StageError(logger, KoanLogStage.Host, action, outcome, context);

    internal static void Write(ILogger? logger, KoanLogStage stage, LogLevel level, string action, string? outcome, (string Key, object? Value)[] context)
    {
        if (logger is null) return;
        logger.LogKoanStage(stage, level, action, outcome, context);
    }

    private static string GetCategoryName(Type type)
    {
        if (type is null) return "Koan";
        return type.FullName ?? type.Name ?? "Koan";
    }

    public sealed class KoanLogScope
    {
        private readonly string _categoryName;
        private ILogger? _logger;

        internal KoanLogScope(string categoryName)
        {
            _categoryName = categoryName;
        }

        private ILogger? ResolveLogger()
        {
            var logger = Volatile.Read(ref _logger);
            if (logger is not null)
            {
                return logger;
            }

            var created = CreateLogger(_categoryName);
            if (created is null)
            {
                return null;
            }

            Interlocked.CompareExchange(ref _logger, created, null);
            return Volatile.Read(ref _logger);
        }

        public void StageDebug(KoanLogStage stage, string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.StageDebug(ResolveLogger(), stage, action, outcome, context);

        public void StageInfo(KoanLogStage stage, string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.StageInfo(ResolveLogger(), stage, action, outcome, context);

        public void StageWarning(KoanLogStage stage, string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.StageWarning(ResolveLogger(), stage, action, outcome, context);

        public void StageError(KoanLogStage stage, string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.StageError(ResolveLogger(), stage, action, outcome, context);

        public void BuildDebug(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.BuildDebug(ResolveLogger(), action, outcome, context);

        public void BuildInfo(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.BuildInfo(ResolveLogger(), action, outcome, context);

        public void BuildWarning(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.BuildWarning(ResolveLogger(), action, outcome, context);

        public void BuildError(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.BuildError(ResolveLogger(), action, outcome, context);

        public void BootDebug(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.BootDebug(ResolveLogger(), action, outcome, context);

        public void BootInfo(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.BootInfo(ResolveLogger(), action, outcome, context);

        public void BootWarning(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.BootWarning(ResolveLogger(), action, outcome, context);

        public void BootError(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.BootError(ResolveLogger(), action, outcome, context);

        public void ConfigDebug(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.ConfigDebug(ResolveLogger(), action, outcome, context);

        public void ConfigInfo(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.ConfigInfo(ResolveLogger(), action, outcome, context);

        public void ConfigWarning(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.ConfigWarning(ResolveLogger(), action, outcome, context);

        public void ConfigError(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.ConfigError(ResolveLogger(), action, outcome, context);

        public void SnapshotDebug(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.SnapshotDebug(ResolveLogger(), action, outcome, context);

        public void SnapshotInfo(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.SnapshotInfo(ResolveLogger(), action, outcome, context);

        public void SnapshotWarning(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.SnapshotWarning(ResolveLogger(), action, outcome, context);

        public void SnapshotError(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.SnapshotError(ResolveLogger(), action, outcome, context);

        public void DataDebug(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.DataDebug(ResolveLogger(), action, outcome, context);

        public void DataInfo(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.DataInfo(ResolveLogger(), action, outcome, context);

        public void DataWarning(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.DataWarning(ResolveLogger(), action, outcome, context);

        public void DataError(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.DataError(ResolveLogger(), action, outcome, context);

        public void ServiceDebug(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.ServiceDebug(ResolveLogger(), action, outcome, context);

        public void ServiceInfo(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.ServiceInfo(ResolveLogger(), action, outcome, context);

        public void ServiceWarning(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.ServiceWarning(ResolveLogger(), action, outcome, context);

        public void ServiceError(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.ServiceError(ResolveLogger(), action, outcome, context);

        public void HealthDebug(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.HealthDebug(ResolveLogger(), action, outcome, context);

        public void HealthInfo(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.HealthInfo(ResolveLogger(), action, outcome, context);

        public void HealthWarning(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.HealthWarning(ResolveLogger(), action, outcome, context);

        public void HealthError(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.HealthError(ResolveLogger(), action, outcome, context);

        public void HostDebug(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.HostDebug(ResolveLogger(), action, outcome, context);

        public void HostInfo(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.HostInfo(ResolveLogger(), action, outcome, context);

        public void HostWarning(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.HostWarning(ResolveLogger(), action, outcome, context);

        public void HostError(string action, string? outcome = null, params (string Key, object? Value)[] context)
            => KoanLog.HostError(ResolveLogger(), action, outcome, context);
    }
}
