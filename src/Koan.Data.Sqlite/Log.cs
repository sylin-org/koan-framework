using Microsoft.Extensions.Logging;

namespace Koan.Data.Sqlite;

internal static class Log
{
    private static readonly Action<ILogger, string, string, bool, bool, Exception?> _ensureTableStart
        = LoggerMessage.Define<string, string, bool, bool>(LogLevel.Debug, new EventId(1000, "sqlite.ensure_table.start"),
            "Ensure table start: table={Table} policy={Policy} ddlAllowed={DdlAllowed} readOnly={ReadOnly}");
    private static readonly Action<ILogger, string, Exception?> _ensureTableCreated
        = LoggerMessage.Define<string>(LogLevel.Information, new EventId(1001, "sqlite.ensure_table.created"),
            "Table created (or exists): table={Table}");
    private static readonly Action<ILogger, string, string, Exception?> _ensureTableSkip
        = LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(1002, "sqlite.ensure_table.skip"),
            "Ensure table skipped: table={Table} reason={Reason}");
    private static readonly Action<ILogger, string, string, string, Exception?> _ensureTableAddColumn
        = LoggerMessage.Define<string, string, string>(LogLevel.Debug, new EventId(1003, "sqlite.ensure_table.add_column"),
            "Added generated column: table={Table} column={Column} path={JsonPath}");
    private static readonly Action<ILogger, string, string, Exception?> _ensureTableAddIndex
        = LoggerMessage.Define<string, string>(LogLevel.Debug, new EventId(1004, "sqlite.ensure_table.add_index"),
            "Ensured index: table={Table} column={Column}");
    private static readonly Action<ILogger, string, Exception?> _ensureTableEnd
        = LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1005, "sqlite.ensure_table.end"),
            "Ensure table end: table={Table}");

    private static readonly Action<ILogger, string, Exception?> _validateSchemaStart
        = LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1100, "sqlite.validate_schema.start"),
            "Validate schema start: table={Table}");
    private static readonly Action<ILogger, string, string, Exception?> _validateSchemaResultInfo
        = LoggerMessage.Define<string, string>(LogLevel.Information, new EventId(1101, "sqlite.validate_schema.result"),
            "Validate schema result: table={Table} state={State}");
    private static readonly Action<ILogger, string, string, Exception?> _validateSchemaResultWarn
        = LoggerMessage.Define<string, string>(LogLevel.Warning, new EventId(1102, "sqlite.validate_schema.result_unhealthy"),
            "Validate schema result: table={Table} state={State}");

    private static readonly Action<ILogger, string, int, Exception?> _retryMissingTable
        = LoggerMessage.Define<string, int>(LogLevel.Information, new EventId(1200, "sqlite.retry_missing_table"),
            "Retry after ensuring missing table: table={Table} code={Code}");

    public static void EnsureTableStart(ILogger logger, string table, string policy, bool ddlAllowed, bool readOnly)
        => _ensureTableStart(logger, table, policy, ddlAllowed, readOnly, null);
    public static void EnsureTableCreated(ILogger logger, string table)
        => _ensureTableCreated(logger, table, null);
    public static void EnsureTableSkip(ILogger logger, string table, string policy, string reason)
        => _ensureTableSkip(logger, table, reason, null);
    public static void EnsureTableAddColumn(ILogger logger, string table, string column, string jsonPath)
        => _ensureTableAddColumn(logger, table, column, jsonPath, null);
    public static void EnsureTableAddIndex(ILogger logger, string table, string column)
        => _ensureTableAddIndex(logger, table, column, null);
    public static void EnsureTableEnd(ILogger logger, string table)
        => _ensureTableEnd(logger, table, null);

    public static void ValidateSchemaStart(ILogger logger, string table)
        => _validateSchemaStart(logger, table, null);
    public static void ValidateSchemaResult(ILogger logger, string table, bool exists, int missing, string policy, bool ddlAllowed, string matching, string state)
    {
        if (string.Equals(state, "Healthy", StringComparison.OrdinalIgnoreCase))
        {
            _validateSchemaResultInfo(logger, table, state, null);
            logger.LogDebug("Validate schema details: table={Table} exists={Exists} missing={Missing} policy={Policy} ddlAllowed={DdlAllowed} matching={Matching}",
                table, exists, missing, policy, ddlAllowed, matching);
        }
        else
        {
            _validateSchemaResultWarn(logger, table, state, null);
            logger.LogDebug("Validate schema details: table={Table} exists={Exists} missing={Missing} policy={Policy} ddlAllowed={DdlAllowed} matching={Matching}",
                table, exists, missing, policy, ddlAllowed, matching);
        }
    }

    public static void RetryMissingTable(ILogger logger, string table, int code)
        => _retryMissingTable(logger, table, code, null);
}