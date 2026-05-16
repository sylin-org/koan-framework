namespace Koan.Cache.Abstractions.Coherence;

/// <summary>
/// Cache-specific specialization of <see cref="ICoherenceChannel{TMessage}"/> carrying
/// <see cref="CacheInvalidation"/> payloads. Adapters implement this interface (not the
/// generic base) so the DI container can resolve them as <c>IEnumerable&lt;ICacheCoherenceChannel&gt;</c>.
/// </summary>
public interface ICacheCoherenceChannel : ICoherenceChannel<CacheInvalidation>
{
}
