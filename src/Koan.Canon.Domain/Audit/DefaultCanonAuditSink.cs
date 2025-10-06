using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Canon.Domain.Model;

namespace Koan.Canon.Domain.Audit;

/// <summary>
/// Default audit sink that persists entries to <see cref="CanonAuditLog"/>.
/// </summary>
public sealed class DefaultCanonAuditSink : ICanonAuditSink
{
    /// <inheritdoc />
    public async Task WriteAsync(IReadOnlyList<CanonAuditEntry> entries, CancellationToken cancellationToken)
    {
        if (entries is null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        if (entries.Count == 0)
        {
            return;
        }

        var models = entries.Select(entry => new CanonAuditLog
        {
            CanonicalId = entry.CanonicalId,
            EntityType = entry.EntityType,
            Property = entry.Property,
            Policy = entry.Policy,
            PreviousValue = entry.PreviousValue,
            CurrentValue = entry.CurrentValue,
            Source = entry.Source,
            ArrivalToken = entry.ArrivalToken,
            OccurredAt = entry.OccurredAt,
            Evidence = new Dictionary<string, string?>(entry.Evidence, StringComparer.OrdinalIgnoreCase)
        }).ToList();

        await CanonAuditLog.UpsertMany(models, cancellationToken).ConfigureAwait(false);
    }
}
