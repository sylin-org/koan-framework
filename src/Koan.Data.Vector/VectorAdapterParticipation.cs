using System.Collections.Concurrent;
using Koan.Data.Vector.Abstractions;

namespace Koan.Data.Vector;

internal sealed class VectorAdapterParticipation : IVectorAdapterParticipation
{
    private readonly ConcurrentDictionary<ParticipationKey, byte> _active = new(ParticipationKeyComparer.Instance);

    public void Observe(string provider, string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        var normalizedSource = string.IsNullOrWhiteSpace(source) ? "Default" : source.Trim();
        _active.TryAdd(new ParticipationKey(provider.Trim(), normalizedSource), 0);
    }

    public IReadOnlyCollection<string> ActiveSources(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider)) return [];

        return _active.Keys
            .Where(key => string.Equals(key.Provider, provider, StringComparison.OrdinalIgnoreCase))
            .Select(static key => key.Source)
            .OrderBy(static source => source, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private readonly record struct ParticipationKey(string Provider, string Source);

    private sealed class ParticipationKeyComparer : IEqualityComparer<ParticipationKey>
    {
        public static ParticipationKeyComparer Instance { get; } = new();

        public bool Equals(ParticipationKey x, ParticipationKey y) =>
            string.Equals(x.Provider, y.Provider, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Source, y.Source, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(ParticipationKey value) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Provider),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.Source));
    }
}
