namespace Koan.Identity.Audit;

/// <summary>Controls how much Entity state Identity writes into audit before/after snapshots.</summary>
public enum IdentityAuditSnapshotMode
{
    /// <summary>Retain only bounded state-transition metadata; exclude identity attributes, factors, and device data.</summary>
    PrivacySafe = 0,

    /// <summary>
    /// Retain complete Entity snapshots for compatibility and forensic use. Provider claim blobs remain redacted,
    /// and identity erasure still sanitizes related retained records.
    /// </summary>
    Full = 1,
}
