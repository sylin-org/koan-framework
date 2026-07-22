using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;

namespace Koan.Packaging.Services;

internal sealed class NativeAdmissionPlanner
{
    public NativeAdmissionPlan Create(
        string baseCommit,
        string candidateCommit,
        IReadOnlyCollection<string> changedPaths,
        IReadOnlyCollection<PackageProject> projects,
        ProductSurface surface)
    {
        var changes = changedPaths
            .Select(Normalize)
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sharedChange = changes.Any(IsSharedInput);
        var changedPackages = projects
            .Where(project => changes.Any(path => TouchesProject(path, project.ProjectPath)))
            .Select(project => project.PackageId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        ExpandReverseDependencies(changedPackages, surface.Packages);

        var affected = surface.Claims
            .Where(claim => sharedChange
                || claim.Packages.Any(changedPackages.Contains)
                || changes.Any(path => TouchesClaim(path, claim)))
            .OrderBy(claim => claim.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var cells = affected
            .SelectMany(claim => (claim.Admission ?? [])
                .Where(cell => cell.Lane == PackagingConstants.Admission.NativeLane)
                .Select(cell => new NativeAdmissionCell(
                    claim.Id,
                    cell.Id,
                    cell.Project,
                    cell.Filter,
                    cell.Lane,
                    cell.Phase,
                    cell.DeadlineSeconds)))
            .OrderBy(cell => cell.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var applicable = cells.Length > 0;
        var reason = applicable
            ? $"{cells.Length} native cell(s) declared by {affected.Length} affected active claim(s)"
            : affected.Length == 0
                ? "no active product claim is affected by the candidate"
                : $"{affected.Length} affected active claim(s) declare no native admission cell";

        return new NativeAdmissionPlan(
            baseCommit,
            candidateCommit,
            applicable
                ? PackagingConstants.Admission.RequiredApplicability
                : PackagingConstants.Admission.NotApplicable,
            changes,
            affected.Select(claim => claim.Id).ToArray(),
            cells,
            reason);
    }

    private static void ExpandReverseDependencies(
        HashSet<string> changedPackages,
        IReadOnlyCollection<ProductPackage> packages)
    {
        var added = true;
        while (added)
        {
            added = false;
            foreach (var package in packages)
            {
                if (!changedPackages.Contains(package.PackageId)
                    && package.Dependencies.Any(changedPackages.Contains))
                {
                    changedPackages.Add(package.PackageId);
                    added = true;
                }
            }
        }
    }

    private static bool TouchesClaim(string changedPath, ProductClaim claim) =>
        claim.Documentation.Concat(claim.Evidence).Any(path => TouchesPath(changedPath, path))
        || (claim.Admission ?? []).Any(cell => TouchesProject(changedPath, cell.Project));

    private static bool TouchesProject(string changedPath, string projectPath)
    {
        var normalizedProject = Normalize(projectPath);
        var directory = Normalize(Path.GetDirectoryName(normalizedProject) ?? string.Empty).TrimEnd('/');
        return changedPath.Equals(normalizedProject, StringComparison.OrdinalIgnoreCase)
            || (directory.Length > 0 && changedPath.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TouchesPath(string changedPath, string declaredPath)
    {
        var normalized = Normalize(declaredPath).TrimEnd('/');
        return changedPath.Equals(normalized, StringComparison.OrdinalIgnoreCase)
            || changedPath.StartsWith(normalized + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSharedInput(string path) =>
        PackagingConstants.Admission.SharedFiles.Contains(path)
        || PackagingConstants.Admission.SharedPrefixes.Any(
            prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static string Normalize(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith("./", StringComparison.Ordinal) ? normalized[2..] : normalized.TrimStart('/');
    }
}
