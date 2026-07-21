using Koan.Core.Hosting.Registry;

namespace Koan.Jobs;

/// <summary>
/// Holds the <see cref="JobTypeBinding"/> for every discovered work-item type, bound once at first use. Discovery
/// rides <c>[KoanDiscoverable]</c> on <see cref="IKoanJob"/> (build-time generator + runtime manifest) — no
/// AppDomain scan. Types that implement <see cref="IKoanJob"/> but not the self-generic <c>IKoanJob&lt;TSelf&gt;</c>
/// shape are skipped.
/// </summary>
internal sealed class JobTypeRegistry
{
    private readonly Dictionary<string, JobTypeBinding> _byType;

    public JobTypeRegistry(IEnumerable<Type> workTypes)
    {
        _byType = workTypes
            .Where(IsValidWorkType)
            .Select(JobTypeBinder.Bind)
            .ToDictionary(b => b.WorkType, StringComparer.Ordinal);
    }

    /// <summary>Build from the framework's discovered <see cref="IKoanJob"/> implementors.</summary>
    public static JobTypeRegistry FromDiscovery()
        => new(KoanRegistry.GetDiscoveredImplementors(typeof(IKoanJob)));

    public JobTypeBinding? Get(string workType) => _byType.TryGetValue(workType, out var b) ? b : null;

    public JobTypeBinding Require(string workType)
        => Get(workType) ?? throw new InvalidOperationException(
            $"No job binding for work-type '{workType}'. Is the type discovered (implements IKoanJob<TSelf> : Entity<TSelf>)?");

    public IReadOnlyCollection<JobTypeBinding> All => _byType.Values;

    public int Count => _byType.Count;

    /// <summary>A type is a valid work-item iff it is a concrete class implementing <c>IKoanJob&lt;itself&gt;</c>.</summary>
    public static bool IsValidWorkType(Type t)
        => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
           && t.GetInterfaces().Any(i =>
               i.IsGenericType
               && i.GetGenericTypeDefinition() == typeof(IKoanJob<>)
               && i.GetGenericArguments()[0] == t);
}
