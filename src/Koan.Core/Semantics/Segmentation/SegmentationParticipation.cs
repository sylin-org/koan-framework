using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Core.Semantics.Segmentation;

/// <summary>
/// Framework-facing declaration that a pillar takes part in hard segmentation and which realization covers it
/// (ARCH-0137). A pillar declares it from <c>Register</c> before any branch that could skip its realization; when the
/// segmentation plan compiles with an active dimension, composition refuses a host in which a declared participant has
/// no registered realization.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class SegmentationParticipation
{
    public SegmentationParticipation(string pillarId, Type realizationType, string correction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pillarId);
        ArgumentNullException.ThrowIfNull(realizationType);
        ArgumentException.ThrowIfNullOrWhiteSpace(correction);
        if (!typeof(ISegmentationRealization).IsAssignableFrom(realizationType))
            throw new ArgumentException(
                $"'{realizationType.FullName}' must implement {nameof(ISegmentationRealization)}.",
                nameof(realizationType));

        PillarId = pillarId.Trim();
        RealizationType = realizationType;
        Correction = correction;
    }

    public string PillarId { get; }

    public Type RealizationType { get; }

    public string Correction { get; }

    /// <summary>Declares <paramref name="participation"/> once per pillar for this collection.</summary>
    public static void Declare(IServiceCollection services, SegmentationParticipation participation)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(participation);
        if (services.Any(descriptor => descriptor.ImplementationInstance is SegmentationParticipation existing
                                       && string.Equals(existing.PillarId, participation.PillarId, StringComparison.Ordinal)))
            return;

        services.AddSingleton(participation);
    }

    /// <summary>The declared participants of <paramref name="services"/> whose realization is not registered.</summary>
    internal static IReadOnlyList<SegmentationParticipation> Unrealized(IServiceCollection services)
    {
        var realized = services
            .Where(static descriptor => descriptor.ServiceType == typeof(ISegmentationRealization))
            .Select(static descriptor => descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType())
            .Where(static type => type is not null)
            .ToHashSet();
        return services
            .Select(static descriptor => descriptor.ImplementationInstance)
            .OfType<SegmentationParticipation>()
            .Where(participation => !realized.Contains(participation.RealizationType))
            .OrderBy(static participation => participation.PillarId, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>Refuses a composition in which an active dimension has a declared participant without a realization.</summary>
    internal static void Verify(IServiceCollection services, SegmentationPlan plan)
    {
        if (plan.IsEmpty) return;
        var missing = Unrealized(services);
        if (missing.Count == 0) return;

        var dimensions = string.Join(", ", plan.Dimensions.Select(static dimension => dimension.Id));
        throw new InvalidOperationException(
            $"Hard segmentation is active for dimension(s) {dimensions}, but " +
            string.Join("; ", missing.Select(static participation =>
                $"pillar '{participation.PillarId}' has no registered realization ('{participation.RealizationType.Name}'). " +
                participation.Correction)) +
            " Koan refuses the host rather than run a pillar without isolation (ARCH-0137).");
    }
}
