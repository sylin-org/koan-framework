using System.Collections.Generic;

namespace Koan.Data.Core.KeyValue;

/// <summary>
/// The internal storage envelope of the <see cref="KeyValueStore{TEntity,TKey}"/> family (ARCH-0103 §9.2): an entity
/// paired with the framework-managed field values stamped onto it at write time (<c>__koan_tenant</c>, <c>__deleted</c>,
/// …). For the object-graph family (InMemory) the <see cref="Managed"/> dictionary is the literal sidecar; for the
/// JSON-text family (Json/Redis) it is the set of <c>__</c>-keys injected into / extracted back from the serialized
/// value. The base reads <see cref="Managed"/> to evaluate the managed read-filter and to guard a cross-scope write.
/// </summary>
/// <param name="Entity">The persisted entity (the POCO).</param>
/// <param name="Managed">The stamped managed-field values (<c>ManagedFieldWriteScope.Effective</c> at write time), or
/// <c>null</c> when the record was written outside any managed scope (the off / host-context case).</param>
public readonly record struct KvRecord<TEntity>(TEntity Entity, IReadOnlyDictionary<string, object?>? Managed);
