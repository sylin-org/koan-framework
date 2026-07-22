using System.Runtime.CompilerServices;
using Koan.Packaging.Models;
using Koan.Packaging.Services;
using Xunit;

namespace Koan.Packaging.Tests;

public sealed class TerminalOutcomeReconcilerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"koan-terminal-outcomes-{Guid.NewGuid():N}");

    public TerminalOutcomeReconcilerTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "docs"));
        File.WriteAllText(Path.Combine(root, "docs", "removal.md"), "evidence");
    }

    [Fact]
    public void Parses_exact_fixed_baseline_from_architecture_decision()
    {
        var decision = File.ReadAllText(RepositoryPath("docs", "decisions", "ARCH-0120-terminal-package-maturity.md"));

        var baseline = TerminalOutcomeReconciler.ParseBaseline(decision);

        Assert.Equal(55, baseline.Count);
        Assert.Equal(55, baseline.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("Sylin.Koan.Testing.Hosting", baseline[0]);
        Assert.Equal("Sylin.Koan.AI.Connector.HuggingFace", baseline[^1]);
    }

    [Fact]
    public void Rejects_mutated_or_truncated_architecture_baseline()
    {
        var decision = File.ReadAllText(RepositoryPath("docs", "decisions", "ARCH-0120-terminal-package-maturity.md"));
        var truncated = decision.Replace(
            "| 55 | 9 | `Sylin.Koan.AI.Connector.HuggingFace` | migrate catalog/provider behavior or reverse its prior disposition |",
            string.Empty,
            StringComparison.Ordinal);

        var error = Assert.Throws<InvalidOperationException>(() => TerminalOutcomeReconciler.ParseBaseline(truncated));

        Assert.Contains("54 owner row", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_certificate_is_valid_only_as_partial_progress()
    {
        var partial = Reconciler().Reconcile(Baseline(), Projects(Baseline()), Surface(), Certificate(), final: false);

        Assert.Equal(55, partial.Remaining);
        Assert.Equal(0, partial.Resolved);
        Assert.Throws<InvalidOperationException>(() =>
            Reconciler().Reconcile(Baseline(), Projects(Baseline()), Surface(), Certificate(), final: true));
    }

    [Fact]
    public void Active_supported_owner_resolves_without_certificate_entry()
    {
        var report = Reconciler().Reconcile(
            Baseline(),
            Projects(Baseline()),
            Surface("Package-01"),
            Certificate(),
            final: false);

        Assert.Equal(1, report.ActiveSupported);
        Assert.Equal(1, report.Resolved);
        Assert.Equal(54, report.Remaining);
    }

    [Fact]
    public void Removed_owner_requires_valid_bounded_evidence()
    {
        var active = Baseline().Skip(1).ToArray();

        var report = Reconciler().Reconcile(
            Baseline(),
            Projects(active),
            Surface(),
            Certificate(Removed("Package-01", "migrated", "https://github.com/sylin-org/replacement")),
            final: false);

        Assert.Equal(1, report.Removed);
        Assert.Equal(54, report.Remaining);
    }

    [Theory]
    [InlineData("unknown", "replacement", "invalid removed-owner disposition")]
    [InlineData("migrated", null, "requires a destination")]
    [InlineData("supported", "replacement", "supported owners stay in product truth")]
    public void Rejects_invalid_removed_disposition(string disposition, string? destination, string expected)
    {
        var outcome = Removed("Package-01", disposition, destination);

        var error = Assert.Throws<InvalidOperationException>(() => Reconciler().Reconcile(
            Baseline(),
            Projects(Baseline().Skip(1)),
            Surface(),
            Certificate(outcome),
            final: false));

        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_unknown_duplicate_and_still_active_removed_owners()
    {
        var unknown = Assert.Throws<InvalidOperationException>(() => Reconciler().Reconcile(
            Baseline(), [], Surface(), Certificate(Removed("Future-Package", "retired", null)), false));
        Assert.Contains("unknown non-baseline", unknown.Message, StringComparison.Ordinal);

        var duplicate = Assert.Throws<InvalidOperationException>(() => Reconciler().Reconcile(
            Baseline(), [], Surface(), Certificate(Removed("Package-01", "retired", null), Removed("Package-01", "retired", null)), false));
        Assert.Contains("more than once", duplicate.Message, StringComparison.Ordinal);

        var active = Assert.Throws<InvalidOperationException>(() => Reconciler().Reconcile(
            Baseline(), Projects(["Package-01"]), Surface(), Certificate(Removed("Package-01", "retired", null)), false));
        Assert.Contains("remains active", active.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_malformed_commit_commands_and_evidence()
    {
        var malformedCommit = Removed("Package-01", "retired", null) with { PublicCommit = "abc" };
        Assert.Contains("exact lowercase 40-character", Failure(malformedCommit), StringComparison.Ordinal);

        var noCommands = Removed("Package-01", "retired", null) with { Commands = [] };
        Assert.Contains("commands", Failure(noCommands), StringComparison.Ordinal);

        var missingEvidence = Removed("Package-01", "retired", null) with { Evidence = ["docs/missing.md"] };
        Assert.Contains("missing path", Failure(missingEvidence), StringComparison.Ordinal);
    }

    [Fact]
    public void Final_mode_passes_only_when_all_baseline_owners_resolve_once()
    {
        var report = Reconciler().Reconcile(
            Baseline(),
            Projects(Baseline()),
            Surface(Baseline().ToArray()),
            Certificate(),
            final: true);

        Assert.True(report.Final);
        Assert.Equal(55, report.Resolved);
        Assert.Equal(0, report.Remaining);
    }

    [Fact]
    public void Future_active_packages_are_outside_the_fixed_epic()
    {
        var projects = Projects(Baseline().Append("Future-Package"));

        var report = Reconciler().Reconcile(Baseline(), projects, Surface("Package-01"), Certificate(), false);

        Assert.Equal(1, report.Resolved);
    }

    [Fact]
    public void Main_pr_gate_runs_partial_reconciliation_before_ratchet()
    {
        var workflow = File.ReadAllText(RepositoryPath(".github", "workflows", "pr-gate.yml"));
        var reconciliation = workflow.IndexOf("terminal-outcomes", StringComparison.Ordinal);
        var ratchet = workflow.IndexOf("./scripts/green-ratchet.ps1", StringComparison.Ordinal);

        Assert.True(reconciliation >= 0);
        Assert.True(ratchet > reconciliation);
        Assert.DoesNotContain("terminal-outcomes --final", workflow, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string Failure(RemovedOwnerOutcome outcome) => Assert.Throws<InvalidOperationException>(() =>
        Reconciler().Reconcile(Baseline(), [], Surface(), Certificate(outcome), false)).Message;

    private TerminalOutcomeReconciler Reconciler() => new(root);

    private static IReadOnlyList<string> Baseline() =>
        Enumerable.Range(1, 55).Select(index => $"Package-{index:00}").ToArray();

    private static IReadOnlyList<PackageProject> Projects(IEnumerable<string> ids) => ids.Select(id => new PackageProject(
        $"src/{id}/{id}.csproj", $"src/{id}", id, "Dependency", ["net10.0"], false, false, true,
        false, true, "README.md", true, "TECHNICAL.md", "Description", "koan", [])).ToArray();

    private static ProductSurface Surface(params string[] supported) => new()
    {
        Source = "product/claims.json",
        Claims = supported.Length == 0
            ? []
            : [new ProductClaim("supported", "Supported", "Outcome", "supported-extension", supported, ["docs"], ["tests"])],
        Packages = []
    };

    private static TerminalOutcomeCertificate Certificate(params RemovedOwnerOutcome[] outcomes) => new()
    {
        SchemaVersion = 1,
        ArchitectureDecision = "ARCH-0120",
        Outcomes = outcomes.ToList()
    };

    private static RemovedOwnerOutcome Removed(string packageId, string disposition, string? destination) => new()
    {
        PackageId = packageId,
        Disposition = disposition,
        Destination = destination,
        PublicCommit = new string('a', 40),
        Commands = ["dotnet test tests/Replacement.Tests.csproj"],
        Evidence = ["docs/removal.md"]
    };

    private static string RepositoryPath(
        string first,
        string second,
        string third,
        [CallerFilePath] string sourceFile = "") =>
        Path.Combine(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..")), first, second, third);
}
