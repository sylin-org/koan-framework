using System;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Koan.Data.Connector.PGVector;

/// <summary>
/// Manages pgvector extension lifecycle: installation, version detection, and validation.
/// Ensures pgvector extension is available before vector operations.
/// </summary>
public sealed class PgVectorExtensionManager
{
    private readonly ILogger<PgVectorExtensionManager>? _logger;
    private bool _extensionEnsured;
    private string? _detectedVersion;

    public PgVectorExtensionManager(ILogger<PgVectorExtensionManager>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Ensures pgvector extension is installed and enabled.
    /// Idempotent - safe to call multiple times.
    /// </summary>
    public async Task EnsureExtensionAsync(NpgsqlConnection conn, CancellationToken ct = default)
    {
        if (_extensionEnsured) return;

        using var activity = PGVectorTelemetry.Activity.StartActivity("vector.ensureExtension");

        try
        {
            // Check if extension already exists
            var exists = await conn.ExecuteScalarAsync<bool>(
                "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'vector')",
                commandTimeout: 30);

            if (exists)
            {
                // Get version
                _detectedVersion = await conn.ExecuteScalarAsync<string>(
                    "SELECT extversion FROM pg_extension WHERE extname = 'vector'",
                    commandTimeout: 30);

                _logger?.LogDebug("pgvector extension already installed (version: {Version})", _detectedVersion);
            }
            else
            {
                // Install extension
                await conn.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS vector", commandTimeout: 60);

                _detectedVersion = await conn.ExecuteScalarAsync<string>(
                    "SELECT extversion FROM pg_extension WHERE extname = 'vector'",
                    commandTimeout: 30);

                _logger?.LogInformation("Installed pgvector extension (version: {Version})", _detectedVersion);
            }

            _extensionEnsured = true;
        }
        catch (PostgresException ex) when (ex.SqlState == "42501") // insufficient_privilege
        {
            _logger?.LogError(
                ex,
                "Insufficient privileges to create pgvector extension. " +
                "Ensure the database user has SUPERUSER or the extension is pre-installed.");
            throw new InvalidOperationException(
                "Cannot create pgvector extension due to insufficient privileges. " +
                "Ask a database administrator to run: CREATE EXTENSION IF NOT EXISTS vector;",
                ex);
        }
        catch (PostgresException ex) when (ex.SqlState == "3D000") // invalid_catalog_name
        {
            _logger?.LogError(
                ex,
                "pgvector extension not found. Ensure it is installed on the PostgreSQL server.");
            throw new InvalidOperationException(
                "pgvector extension is not installed on the PostgreSQL server. " +
                "Install it using: https://github.com/pgvector/pgvector#installation",
                ex);
        }
    }

    /// <summary>
    /// Gets the detected pgvector extension version.
    /// Returns null if extension not yet ensured.
    /// </summary>
    public string? DetectedVersion => _detectedVersion;

    /// <summary>
    /// Validates that pgvector extension supports the specified dimension.
    /// pgvector supports dimensions up to 16,000 (as of v0.6.0+).
    /// </summary>
    public bool ValidateDimension(int dimension)
    {
        if (dimension <= 0)
        {
            _logger?.LogWarning("Invalid dimension {Dimension} (must be > 0)", dimension);
            return false;
        }

        if (dimension > PGVectorOptions.MaxDimension)
        {
            _logger?.LogWarning(
                "Dimension {Dimension} exceeds pgvector maximum ({Max}). " +
                "Consider dimensionality reduction.",
                dimension,
                PGVectorOptions.MaxDimension);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Gets recommended lists parameter for IVFFlat index based on dataset size.
    /// Rule of thumb: lists = sqrt(rows)
    /// </summary>
    public static int GetRecommendedLists(long estimatedRows)
    {
        if (estimatedRows <= 0) return 100; // Default for unknown size

        var lists = (int)Math.Sqrt(estimatedRows);

        // Clamp to reasonable range
        return Math.Clamp(lists, 10, 10000);
    }

    /// <summary>
    /// Gets recommended HNSW parameters based on dataset characteristics.
    /// </summary>
    public static (int m, int efConstruction) GetRecommendedHnswParams(
        long estimatedRows,
        int dimension)
    {
        // Default: m=16, ef_construction=64 (balanced)

        // For large datasets or high dimensions, increase parameters
        if (estimatedRows > 1_000_000 || dimension > 1024)
        {
            return (m: 32, efConstruction: 128); // Better recall, slower build
        }

        if (estimatedRows < 10_000)
        {
            return (m: 8, efConstruction: 32); // Faster build for small datasets
        }

        return (m: 16, efConstruction: 64); // Default
    }
}
