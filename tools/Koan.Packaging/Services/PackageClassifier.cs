using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;

namespace Koan.Packaging.Services;

internal static class PackageClassifier
{
    public static string ShapeOf(PackageProject project, PackageGraph graph)
    {
        var packageTypes = project.PackageType.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (packageTypes.Contains("Template", StringComparer.OrdinalIgnoreCase)) return "template";
        if (project.PackAsTool) return "tool";
        if (project.IsRoslynComponent) return "analyzer";
        if (!project.IncludeBuildOutput && graph.PackageDependenciesOf(project.PackageId).Count > 0) return "bundle";
        if (!project.IncludeBuildOutput) return "content";
        return "library";
    }

    public static string RoleOf(PackageProject project, PackageGraph graph)
    {
        var shape = ShapeOf(project, graph);
        if (shape is "template" or "tool" or "analyzer" or "content") return shape;
        if (shape == "bundle") return PackagingConstants.PackageQuality.EntryRole;

        var id = project.PackageId;
        if (IsFoundation(id)) return PackagingConstants.PackageQuality.FoundationRole;
        if (HasSegment(id, "Contracts") || HasSegment(id, "Abstractions"))
            return PackagingConstants.PackageQuality.ContractsRole;
        if (HasSegment(id, "Connector") || HasSegment(id, "Adapter"))
            return PackagingConstants.PackageQuality.ProviderRole;
        if (IsProjection(id)) return PackagingConstants.PackageQuality.ProjectionRole;
        return PackagingConstants.PackageQuality.CapabilityRole;
    }

    private static bool IsFoundation(string packageId) =>
        string.Equals(packageId, PackagingConstants.PackagePrefix, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(packageId, $"{PackagingConstants.PackagePrefix}.App", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(packageId, PackagingConstants.CorePackageId, StringComparison.OrdinalIgnoreCase);

    private static bool IsProjection(string packageId)
    {
        if (string.Equals(packageId, $"{PackagingConstants.PackagePrefix}.Web", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(packageId, $"{PackagingConstants.PackagePrefix}.Web.Extensions", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return HasSegment(packageId, "Web") ||
               HasSegment(packageId, "Aspire") ||
               HasSegment(packageId, "OpenApi") ||
               HasSegment(packageId, "OpenGraph");
    }

    private static bool HasSegment(string packageId, string segment) =>
        packageId.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Contains(segment, StringComparer.OrdinalIgnoreCase);
}
