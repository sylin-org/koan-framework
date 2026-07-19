using System.Text;
using System.Text.Json;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;

namespace Koan.Packaging.Services;

internal sealed class ProductSurfaceCompiler(string repositoryRoot)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<ProductSurface> CompileAsync(
        IReadOnlyCollection<PackageProject> projects,
        CancellationToken cancellationToken)
    {
        var claimsPath = Path.Combine(repositoryRoot, PackagingConstants.ProductSurface.ClaimsPath);
        if (!File.Exists(claimsPath))
        {
            throw new InvalidOperationException(
                $"Product claims are missing at '{PackagingConstants.ProductSurface.ClaimsPath}'. " +
                "Add the repository-owned claims file; package availability must not imply support.");
        }

        await using var stream = File.OpenRead(claimsPath);
        var source = await JsonSerializer.DeserializeAsync<ProductClaims>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Product claims at '{PackagingConstants.ProductSurface.ClaimsPath}' are empty.");
        return Compile(projects, source);
    }

    internal ProductSurface Compile(IReadOnlyCollection<PackageProject> projects, ProductClaims source)
    {
        if (source.SchemaVersion != PackagingConstants.ProductSurface.Schema)
        {
            throw new InvalidOperationException(
                $"Product claims schema {source.SchemaVersion} is unsupported; expected " +
                $"{PackagingConstants.ProductSurface.Schema}.");
        }

        var graph = new PackageGraph(projects);
        var claimsById = new Dictionary<string, ProductClaim>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in source.Claims.OrderBy(claim => claim.Id, StringComparer.OrdinalIgnoreCase))
        {
            var id = RequireText(input.Id, "claim id");
            if (!claimsById.TryAdd(id, CompileClaim(input, graph)))
            {
                throw new InvalidOperationException($"Product claim '{id}' is declared more than once.");
            }
        }

        ValidateSupportedBoundary(graph, claimsById.Values);

        var claimIdsByPackage = claimsById.Values
            .SelectMany(claim => claim.Packages.Select(packageId => (packageId, claim.Id)))
            .GroupBy(pair => pair.packageId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group.Select(pair => pair.Id)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var packages = graph.Projects
            .OrderBy(project => project.PackageId, StringComparer.OrdinalIgnoreCase)
            .Select(project => new ProductPackage(
                project.PackageId,
                project.VersionIntent,
                PackageClassifier.ShapeOf(project, graph),
                project.Description,
                project.TargetFrameworks,
                graph.PackageDependenciesOf(project.PackageId),
                project.OwnsReadme ? RepositoryPath(Path.Combine(project.ProjectDirectory, project.Readme!)) : null,
                project.OwnsReadme,
                project.TechnicalDocumentation,
                claimIdsByPackage.GetValueOrDefault(project.PackageId) ?? []))
            .ToList();

        return new ProductSurface
        {
            Source = PackagingConstants.ProductSurface.ClaimsPath,
            Claims = claimsById.Values.OrderBy(claim => claim.Id, StringComparer.OrdinalIgnoreCase).ToList(),
            Packages = packages
        };
    }

    public static string ToJson(ProductSurface surface) =>
        JsonSerializer.Serialize(surface, JsonOptions) + Environment.NewLine;

    public static string ToMarkdown(ProductSurface surface)
    {
        var markdown = new StringBuilder();
        markdown.AppendLine("---");
        markdown.AppendLine("type: REFERENCE");
        markdown.AppendLine("domain: framework");
        markdown.AppendLine("title: \"Koan Product Surface\"");
        markdown.AppendLine("audience: [developers, support-engineers, architects, ai-agents]");
        markdown.AppendLine("status: current");
        markdown.AppendLine("last_updated: 2026-07-17");
        markdown.AppendLine("framework_version: source-first");
        markdown.AppendLine("validation:");
        markdown.AppendLine("  date_last_tested: 2026-07-17");
        markdown.AppendLine("  status: passed");
        markdown.AppendLine("  scope: deterministic product claims and evaluated package graph");
        markdown.AppendLine("---");
        markdown.AppendLine();
        markdown.AppendLine("# Koan product surface");
        markdown.AppendLine();
        markdown.AppendLine("> Generated by `Koan.Packaging product-surface` from standard project metadata and `product/claims.json`. Do not edit by hand.");
        markdown.AppendLine();
        markdown.AppendLine("Package availability is not a support promise. Claims below state their current evidence maturity; packages without a claim are explicitly unassessed.");
        markdown.AppendLine();
        markdown.AppendLine("## Assessed capabilities");
        markdown.AppendLine();
        markdown.AppendLine("| Capability | Maturity | Package owners | Documentation / evidence |");
        markdown.AppendLine("|---|---|---|---|");
        foreach (var claim in surface.Claims)
        {
            markdown.Append("| ").Append(Escape(claim.Title)).Append(" — ").Append(Escape(claim.Summary))
                .Append(" | `").Append(claim.Maturity).Append("` | ")
                .Append(string.Join("<br>", claim.Packages.Select(package => $"`{Escape(package)}`")))
                .Append(" | ")
                .Append(string.Join("<br>", claim.Documentation
                    .Select(path => $"Docs: [{Escape(Path.GetFileName(path))}](../../{path})")
                    .Concat(claim.Evidence.Select(path => $"Evidence: [{Escape(Path.GetFileName(path))}](../../{path})"))))
                .AppendLine(" |");
        }

        markdown.AppendLine();
        markdown.AppendLine("## Installable packages");
        markdown.AppendLine();
        markdown.AppendLine("| Package | Version line | Shape / target | Product claim | Package docs |");
        markdown.AppendLine("|---|---|---|---|---|");
        foreach (var package in surface.Packages)
        {
            var targets = package.TargetFrameworks.Count == 0
                ? package.Shape
                : $"{package.Shape}<br>{string.Join(", ", package.TargetFrameworks.Select(Escape))}";
            var claims = package.Claims.Count == 0
                ? $"`{PackagingConstants.ProductSurface.UnassessedMaturity}`"
                : string.Join("<br>", package.Claims.Select(Escape));
            var docs = package.Readme is null
                ? "missing owned README"
                : $"[README](../../{package.Readme})" +
                  (package.TechnicalDocumentation is null
                      ? string.Empty
                      : $"<br>[TECHNICAL](../../{package.TechnicalDocumentation})");
            markdown.Append("| `").Append(Escape(package.PackageId)).Append("` — ")
                .Append(Escape(package.Description)).Append(" | `")
                .Append(Escape(package.VersionIntent ?? "unknown")).Append("` | ")
                .Append(targets).Append(" | ")
                .Append(claims).Append(" | ").Append(docs).AppendLine(" |");
        }

        return markdown.ToString();
    }

    private ProductClaim CompileClaim(ProductClaimInput input, PackageGraph graph)
    {
        var id = RequireText(input.Id, "claim id");
        var title = RequireText(input.Title, $"title for claim '{id}'");
        var summary = RequireText(input.Summary, $"summary for claim '{id}'");
        var maturity = RequireText(input.Maturity, $"maturity for claim '{id}'").ToLowerInvariant();
        if (!PackagingConstants.ProductSurface.Maturities.Contains(maturity))
        {
            throw new InvalidOperationException(
                $"Product claim '{id}' has unknown maturity '{input.Maturity}'. Use the canonical maturity vocabulary.");
        }

        var packages = RequireDistinct(input.Packages, $"packages for claim '{id}'");
        foreach (var packageId in packages)
        {
            var package = graph.Project(packageId);
            if (PackagingConstants.ProductSurface.PromotedMaturities.Contains(maturity) && !package.OwnsReadme)
            {
                throw new InvalidOperationException(
                    $"Product claim '{id}' promotes '{package.PackageId}' as '{maturity}', but the package has no " +
                    "owned README. Add package-owned documentation before making a support promise.");
            }
        }

        var documentation = RequirePaths(input.Documentation, $"documentation for claim '{id}'");
        var evidence = RequirePaths(input.Evidence, $"evidence for claim '{id}'");
        return new ProductClaim(id, title, summary, maturity, packages, documentation, evidence);
    }

    private static void ValidateSupportedBoundary(PackageGraph graph, IEnumerable<ProductClaim> claims)
    {
        var supportedPackages = claims
            .Where(claim => PackagingConstants.ProductSurface.PromotedMaturities.Contains(claim.Maturity))
            .SelectMany(claim => claim.Packages)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var packageId in supportedPackages.Order(StringComparer.OrdinalIgnoreCase))
        {
            var project = graph.Project(packageId);
            if (!string.Equals(project.VersionIntent, "0.20", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Supported package '{packageId}' declares version intent '{project.VersionIntent ?? "<missing>"}'. " +
                    "Set its project-local version.json intent to '0.20'; supported claims and the 0.20 signal must agree.");
            }

            foreach (var dependency in graph.PackageDependenciesOf(packageId))
            {
                if (!supportedPackages.Contains(dependency))
                {
                    throw new InvalidOperationException(
                        $"Supported package '{packageId}' publicly depends on '{dependency}', but that dependency is not " +
                        "owned by a supported claim. Admit the dependency's real guarantee or withhold the dependent package.");
                }
            }
        }

        foreach (var project in graph.Projects.OrderBy(project => project.PackageId, StringComparer.OrdinalIgnoreCase))
        {
            if (string.Equals(project.VersionIntent, "0.20", StringComparison.Ordinal) &&
                !supportedPackages.Contains(project.PackageId))
            {
                throw new InvalidOperationException(
                    $"Package '{project.PackageId}' declares 0.20 version intent but belongs to no supported claim. " +
                    "Add an accepted guarantee or keep the package on its current lower maturity line.");
            }
        }
    }

    private IReadOnlyList<string> RequirePaths(IEnumerable<string> paths, string field)
    {
        var values = RequireDistinct(paths, field);
        foreach (var path in values)
        {
            var fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, path));
            if (!IsWithinRepository(fullPath) || (!File.Exists(fullPath) && !Directory.Exists(fullPath)))
            {
                throw new InvalidOperationException(
                    $"Product {field} references missing repository path '{path}'. Add durable public evidence or correct the claim.");
            }
        }
        return values.Select(RepositoryPath).ToArray();
    }

    private static IReadOnlyList<string> RequireDistinct(IEnumerable<string> values, string field)
    {
        var result = values.Select(value => RequireText(value, field))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (result.Length == 0) throw new InvalidOperationException($"Product {field} must contain at least one value.");
        return result;
    }

    private static string RequireText(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Product {field} is required.")
            : value.Trim();

    private bool IsWithinRepository(string path)
    {
        var root = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private string RepositoryPath(string path) =>
        Path.GetRelativePath(repositoryRoot, Path.IsPathRooted(path) ? path : Path.Combine(repositoryRoot, path))
            .Replace('\\', '/');

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal);
}
