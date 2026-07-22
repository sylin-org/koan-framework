using System.Text.Json;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;

namespace Koan.Packaging.Services;

internal sealed class PackageBaselineValidator(
    Func<string, CancellationToken, Task<IReadOnlyList<string>>> publicVersions)
{
    public async Task<PackageValidationReport> ValidateAsync(
        IReadOnlyCollection<PackageProject> projects,
        ProductSurface surface,
        CancellationToken cancellationToken)
    {
        var supported = surface.Claims
            .Where(claim => PackagingConstants.ProductSurface.PromotedMaturities.Contains(claim.Maturity))
            .SelectMany(claim => claim.Packages)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var projectsById = projects.ToDictionary(project => project.PackageId, StringComparer.OrdinalIgnoreCase);
        var configured = 0;
        var pending = 0;
        var contentOnly = 0;

        foreach (var packageId in supported.Order(StringComparer.OrdinalIgnoreCase))
        {
            var project = projectsById[packageId];
            if (!project.IncludeBuildOutput)
            {
                contentOnly++;
                if (!string.IsNullOrWhiteSpace(project.PackageValidationBaselineVersion))
                {
                    throw new InvalidOperationException(
                        $"Content-only supported package '{packageId}' declares PackageValidationBaselineVersion. " +
                        "Remove the API baseline and retain artifact/dependency-shape plus isolated-consumer checks.");
                }
                continue;
            }

            var versions = StablePreviewVersions(await publicVersions(packageId, cancellationToken));
            var baseline = project.PackageValidationBaselineVersion;
            if (string.IsNullOrWhiteSpace(baseline))
            {
                if (versions.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Supported package '{packageId}' already has public 0.20 artifact '{versions[0]}' but its " +
                        $"owning project '{project.ProjectPath}' has no PackageValidationBaselineVersion. Record " +
                        "the earliest immutable 0.20 artifact before publishing another patch.");
                }

                pending++;
                continue;
            }

            var canonicalBaseline = ParseStablePreviewVersion(baseline, packageId);
            if (!project.EnablePackageValidation)
            {
                throw new InvalidOperationException(
                    $"Supported package '{packageId}' records baseline '{baseline}' but EnablePackageValidation " +
                    "is not active. Restore the repository package-validation policy before publishing.");
            }
            if (versions.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Supported package '{packageId}' records baseline '{baseline}', but NuGet exposes no stable " +
                    "0.20 artifact. Correct the project-local baseline or the public package identity.");
            }
            if (versions[0] != canonicalBaseline)
            {
                throw new InvalidOperationException(
                    $"Supported package '{packageId}' records baseline '{baseline}', but its earliest immutable " +
                    $"public 0.20 artifact is '{versions[0]}'. Use the first artifact; later patches are not the compatibility floor.");
            }

            configured++;
        }

        return new PackageValidationReport(supported.Count, supported.Count - contentOnly, configured, pending, contentOnly);
    }

    public static async Task<IReadOnlyList<string>> ReadNuGetVersionsAsync(
        HttpClient client,
        string packageId,
        CancellationToken cancellationToken)
    {
        var path = $"{packageId.ToLowerInvariant()}/{PackagingConstants.PackageValidation.NuGetVersionsIndexFile}";
        using var response = await client.GetAsync(path, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return [];
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.GetProperty("versions")
            .EnumerateArray()
            .Select(element => element.GetString())
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Cast<string>()
            .ToArray();
    }

    private static IReadOnlyList<Version> StablePreviewVersions(IEnumerable<string> versions) =>
        versions
            .Select(version => Version.TryParse(version, out var parsed) &&
                               parsed.Build >= 0 && parsed.Revision < 0 &&
                               parsed.Major == 0 && parsed.Minor == 20 &&
                               string.Equals(parsed.ToString(3), version, StringComparison.Ordinal)
                ? parsed
                : null)
            .Where(version => version is not null)
            .Cast<Version>()
            .Distinct()
            .Order()
            .ToArray();

    private static Version ParseStablePreviewVersion(string value, string owner)
    {
        if (!Version.TryParse(value, out var parsed) || parsed.Build < 0 || parsed.Revision >= 0 ||
            parsed.Major != 0 || parsed.Minor != 20 ||
            !string.Equals(parsed.ToString(3), value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Package validation baseline '{value}' for '{owner}' is invalid. " +
                "Use the exact stable version format 0.20.patch.");
        }

        return parsed;
    }
}
