using Koan.Core.Composition;
using Koan.Core.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Core.Semantics.Segmentation;

/// <summary>Projects the compiled hard-segmentation contract and pillar receipts into shared runtime facts.</summary>
internal static class SegmentationCompositionFacts
{
    public static void Project(KoanCompositionBuilder builder, IServiceProvider services)
    {
        var plan = services.GetService<SegmentationPlan>();
        if (plan is null || plan.IsEmpty) return;

        var dimensions = plan.Dimensions
            .Select(static dimension => dimension.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();
        builder.AddCapability("segmentation:dimensions", dimensions);
        builder.AddObservation(
            Constants.Diagnostics.Codes.SegmentationDimensionsActive,
            "segmentation:dimensions",
            $"Hard segmentation is active for dimension(s): {string.Join(", ", dimensions)}.",
            Constants.Diagnostics.Reasons.SegmentationCompiled,
            typeof(SegmentationCompositionFacts).FullName);

        var realizations = services.GetServices<ISegmentationRealization>()
            .Select(static realization => realization.SegmentationRealization)
            .OrderBy(static receipt => receipt.PillarId, StringComparer.Ordinal)
            .ToArray();
        foreach (var group in realizations.GroupBy(static receipt => receipt.PillarId, StringComparer.Ordinal))
        {
            var receipts = group.ToArray();
            var subject = $"segmentation:{group.Key}";
            if (receipts.Length != 1)
            {
                builder.AddRejection(
                    subject,
                    Constants.Diagnostics.Reasons.SegmentationRealizationConflict,
                    $"Ensure exactly one hard-segmentation realization is registered for pillar '{group.Key}'.",
                    typeof(SegmentationCompositionFacts).FullName,
                    Constants.Diagnostics.Codes.SegmentationRealizationRejected);
                continue;
            }

            var receipt = receipts[0];
            builder.AddCapability(subject, receipt.CoverageIds
                .Append($"realization.{receipt.RealizationId}")
                .Append("guarantee.enforced-or-rejected"));
            builder.AddGuarantee(
                Constants.Diagnostics.Codes.SegmentationRealizationActive,
                subject,
                $"Hard segmentation is enforced-or-rejected through '{receipt.RealizationId}' for: " +
                $"{string.Join(", ", receipt.CoverageIds)}.",
                Constants.Diagnostics.Reasons.SegmentationRealizationInstalled,
                typeof(SegmentationCompositionFacts).FullName);
        }
    }
}
