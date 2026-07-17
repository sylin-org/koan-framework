using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Canon;

/// <summary>
/// Contract for persisting or forwarding canon audit entries.
/// </summary>
public interface ICanonAuditSink
{
    /// <summary>
    /// Writes audit entries.
    /// </summary>
    Task Write(IReadOnlyList<CanonAuditEntry> entries, CancellationToken cancellationToken);
}
