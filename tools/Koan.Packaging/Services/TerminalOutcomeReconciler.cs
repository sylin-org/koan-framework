using System.Text.Json;
using System.Text.RegularExpressions;
using Koan.Packaging.Infrastructure;
using Koan.Packaging.Models;

namespace Koan.Packaging.Services;

internal sealed class TerminalOutcomeReconciler(string repositoryRoot)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
    private static readonly Regex BaselineRow = new(
        @"^\|\s*(?<position>\d+)\s*\|\s*(?<wave>\d+)\s*\|\s*`(?<package>[^`]+)`\s*\|",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex FullCommit = new(
        "^[0-9a-f]{40}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<TerminalOutcomeReport> ReconcileAsync(
        IReadOnlyCollection<PackageProject> projects,
        ProductSurface surface,
        bool final,
        CancellationToken cancellationToken)
    {
        var decisionPath = Path.Combine(repositoryRoot, PackagingConstants.TerminalOutcomes.ArchitectureDecisionPath);
        var certificatePath = Path.Combine(repositoryRoot, PackagingConstants.TerminalOutcomes.CertificatePath);
        var baseline = ParseBaseline(await File.ReadAllTextAsync(decisionPath, cancellationToken));
        await using var stream = File.OpenRead(certificatePath);
        var certificate = await JsonSerializer.DeserializeAsync<TerminalOutcomeCertificate>(
            stream,
            JsonOptions,
            cancellationToken) ?? throw new InvalidOperationException("R13 terminal-outcomes certificate is empty.");
        return Reconcile(baseline, projects, surface, certificate, final);
    }

    internal TerminalOutcomeReport Reconcile(
        IReadOnlyList<string> baseline,
        IReadOnlyCollection<PackageProject> projects,
        ProductSurface surface,
        TerminalOutcomeCertificate certificate,
        bool final)
    {
        ValidateCertificateHeader(certificate);
        if (baseline.Count != PackagingConstants.TerminalOutcomes.BaselineCount)
        {
            throw new InvalidOperationException(
                $"{PackagingConstants.TerminalOutcomes.ArchitectureDecision} baseline has {baseline.Count} owner(s); " +
                $"expected exactly {PackagingConstants.TerminalOutcomes.BaselineCount}.");
        }

        var baselineSet = baseline.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (baselineSet.Count != baseline.Count)
            throw new InvalidOperationException($"{PackagingConstants.TerminalOutcomes.ArchitectureDecision} baseline contains duplicate package owners.");
        var activeProjects = projects.Select(project => project.PackageId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activeSupported = surface.Claims
            .Where(claim => PackagingConstants.ProductSurface.PromotedMaturities.Contains(claim.Maturity))
            .SelectMany(claim => claim.Packages)
            .Where(baselineSet.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var outcome in certificate.Outcomes)
        {
            var packageId = RequireText(outcome.PackageId, "packageId");
            if (!baselineSet.Contains(packageId))
                throw new InvalidOperationException($"Terminal outcome references unknown non-baseline owner '{packageId}'.");
            if (!removed.Add(packageId))
                throw new InvalidOperationException($"Terminal outcome for '{packageId}' is declared more than once.");
            if (activeProjects.Contains(packageId))
                throw new InvalidOperationException($"Terminal outcome for '{packageId}' is invalid because the package remains active.");

            var disposition = RequireText(outcome.Disposition, $"disposition for '{packageId}'").ToLowerInvariant();
            if (!PackagingConstants.TerminalOutcomes.RemovedDispositions.Contains(disposition))
            {
                throw new InvalidOperationException(
                    $"Terminal outcome for '{packageId}' has invalid removed-owner disposition '{outcome.Disposition}'. " +
                    "Use absorbed, migrated, or retired; supported owners stay in product truth.");
            }
            if (disposition is "absorbed" or "migrated" && string.IsNullOrWhiteSpace(outcome.Destination))
                throw new InvalidOperationException($"Terminal outcome for '{packageId}' disposition '{disposition}' requires a destination.");
            var commit = RequireText(outcome.PublicCommit, $"publicCommit for '{packageId}'");
            if (!FullCommit.IsMatch(commit))
                throw new InvalidOperationException($"Terminal outcome for '{packageId}' requires an exact lowercase 40-character public commit.");
            RequireNonEmptyDistinct(outcome.Commands, $"commands for '{packageId}'");
            var evidence = RequireNonEmptyDistinct(outcome.Evidence, $"evidence for '{packageId}'");
            foreach (var item in evidence) ValidateEvidence(packageId, item);
        }

        var resolved = activeSupported.Concat(removed).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var remaining = baseline.Where(packageId => !resolved.Contains(packageId)).ToArray();
        if (final && remaining.Length > 0)
        {
            throw new InvalidOperationException(
                $"Final R13 reconciliation is missing {remaining.Length} baseline owner(s): {string.Join(", ", remaining)}.");
        }

        return new TerminalOutcomeReport(
            PackagingConstants.TerminalOutcomes.ArchitectureDecision,
            baseline.Count,
            activeSupported.Count,
            removed.Count,
            resolved.Count,
            remaining.Length,
            final,
            remaining);
    }

    internal static IReadOnlyList<string> ParseBaseline(string decision)
    {
        var lines = decision.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var header = Array.FindIndex(
            lines,
            line => line.Trim().Equals(
                PackagingConstants.TerminalOutcomes.BaselineTableHeader,
                StringComparison.Ordinal));
        if (header < 0)
            throw new InvalidOperationException($"Could not find the fixed baseline table in {PackagingConstants.TerminalOutcomes.ArchitectureDecision}.");

        var rows = new List<(int Position, string Package)>();
        foreach (var line in lines.Skip(header + 1))
        {
            var match = BaselineRow.Match(line.Trim());
            if (!match.Success)
            {
                if (rows.Count > 0) break;
                continue;
            }
            rows.Add((int.Parse(match.Groups["position"].Value), match.Groups["package"].Value.Trim()));
        }

        if (rows.Count != PackagingConstants.TerminalOutcomes.BaselineCount)
        {
            throw new InvalidOperationException(
                $"{PackagingConstants.TerminalOutcomes.ArchitectureDecision} fixed table contains {rows.Count} owner row(s); " +
                $"expected {PackagingConstants.TerminalOutcomes.BaselineCount}.");
        }
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index].Position != index + 1)
                throw new InvalidOperationException($"R13 baseline position {index + 1} is missing or out of order.");
        }
        return rows.Select(row => row.Package).ToArray();
    }

    private static void ValidateCertificateHeader(TerminalOutcomeCertificate certificate)
    {
        if (certificate.SchemaVersion != PackagingConstants.TerminalOutcomes.Schema)
            throw new InvalidOperationException($"Terminal-outcomes schema {certificate.SchemaVersion} is unsupported.");
        if (!certificate.ArchitectureDecision.Equals(
                PackagingConstants.TerminalOutcomes.ArchitectureDecision,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Terminal-outcomes certificate must bind to {PackagingConstants.TerminalOutcomes.ArchitectureDecision}.");
        }
    }

    private void ValidateEvidence(string packageId, string evidence)
    {
        if (Uri.TryCreate(evidence, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme == Uri.UriSchemeHttps) return;
            throw new InvalidOperationException($"Terminal outcome evidence for '{packageId}' must use a public HTTPS URI.");
        }

        var path = Path.GetFullPath(Path.Combine(repositoryRoot, evidence));
        var root = Path.GetFullPath(repositoryRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || (!File.Exists(path) && !Directory.Exists(path)))
            throw new InvalidOperationException($"Terminal outcome evidence for '{packageId}' references missing path '{evidence}'.");
    }

    private static IReadOnlyList<string> RequireNonEmptyDistinct(IEnumerable<string> values, string field)
    {
        var items = values.Select(value => RequireText(value, field)).ToArray();
        if (items.Length == 0) throw new InvalidOperationException($"Terminal outcome {field} must contain at least one value.");
        if (items.Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Length)
            throw new InvalidOperationException($"Terminal outcome {field} contains duplicate values.");
        return items;
    }

    private static string RequireText(string? value, string field) => string.IsNullOrWhiteSpace(value)
        ? throw new InvalidOperationException($"Terminal outcome {field} is required.")
        : value.Trim();
}
