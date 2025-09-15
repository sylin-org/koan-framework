namespace Koan.Data.Sqlite;

internal static class SqliteTelemetry
{
    public static readonly System.Diagnostics.ActivitySource Activity = new("Koan.Data.Sqlite");
}

// legacy initializer removed in favor of standardized auto-registrar

// Bridge SqliteOptions governance into Relational orchestrator options so validation/ensure semantics are consistent

// Structured logging helpers for SQLite adapter (reusable, allocation-free)