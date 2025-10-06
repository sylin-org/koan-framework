using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Canon.Domain.Audit;

/// <summary>
/// Contract for persisting or forwarding canon audit entries.
/// </summary>
public interface ICanonAuditSink
{
    /// <summary>
    /// Writes audit entries.
    /// </summary>
    Task WriteAsync(IReadOnlyList<CanonAuditEntry> entries, CancellationToken cancellationToken);
}
