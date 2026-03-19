namespace Koan.Storage.Connector.S3;

/// <summary>
/// Configuration for S3-compatible storage.
///
/// When connected via Zen Garden, Endpoint is resolved from the garden's
/// S3 port catalog. When standalone, configure explicitly.
/// </summary>
public sealed class S3StorageOptions
{
    /// <summary>
    /// S3 endpoint URL (e.g., "http://stone-01.local:23454").
    /// Resolved from Zen Garden when using "zen-garden://" connection string.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// Access key for S3 authentication.
    /// Not required for Zen Garden unsigned mode.
    /// </summary>
    public string? AccessKey { get; set; }

    /// <summary>
    /// Secret key for S3 authentication.
    /// Not required for Zen Garden unsigned mode.
    /// </summary>
    public string? SecretKey { get; set; }

    /// <summary>
    /// Prefix for bucket names, derived from AppIdentity.Code.
    /// Example: "snap-vault" → bucket "snap-vault-photos".
    /// </summary>
    public string? BucketPrefix { get; set; }

    /// <summary>
    /// Use HTTPS for S3 connections. Default: false (Zen Garden uses HTTP internally).
    /// </summary>
    public bool UseSsl { get; set; }

    /// <summary>
    /// AWS region (for non-garden S3 endpoints). Default: "us-east-1".
    /// </summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>
    /// Moss HTTP endpoint for presigned URL generation (e.g., "http://stone-01.local:7185").
    /// Resolved from Zen Garden or derived from the S3 endpoint when connected via garden.
    /// Required for <see cref="Koan.Storage.Abstractions.IPresignOperations"/>.
    /// </summary>
    public string? MossEndpoint { get; set; }

    /// <summary>
    /// Seed-bank replica set name for ZenGarden subscriptions.
    /// Null defaults to "storage" (the Zen Garden default).
    /// </summary>
    public string? ReplicaSet { get; set; }
}
