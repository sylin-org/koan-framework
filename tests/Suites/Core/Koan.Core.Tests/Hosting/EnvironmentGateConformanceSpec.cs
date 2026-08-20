using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Xunit;

namespace Koan.Core.Tests.Hosting;

/// <summary>
/// The structural half of ARCH-0128. <see cref="EnvironmentGateSpec"/> pins what the gate does; this spec
/// pins that capabilities actually use it.
///
/// <para>A written law nobody reads is a law nobody follows. The drift ARCH-0128 fixed happened while the
/// rule was already documented, because a direct <c>IsDevelopment()</c> read compiles, passes review, and
/// looks exactly like the correct code. So the check is structural: every direct read of the environment in
/// <c>src/</c> is enumerated below with the reason it is allowed, and a new one fails the build with an
/// explanation of which gate to use instead.</para>
///
/// <para>This is a ratchet in both directions. Adding a read fails; removing one from an allowed file also
/// fails, so the list cannot quietly accumulate entries that stopped being true.</para>
/// </summary>
public sealed class EnvironmentGateConformanceSpec
{
    /// <summary>
    /// Every file in <c>src/</c> permitted to read the environment directly, with its per-file count and the
    /// reason it does not go through <see cref="KoanEnv.Gate"/>.
    ///
    /// <para><b>Adding an entry here is a design decision, not a formality.</b> The question to answer first
    /// is which named decision you are making — see the failure message below. Diagnostics (log level,
    /// banner, health detail) legitimately read the environment; capability gating does not.</para>
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (int Count, string Why)> Allowed =
        new Dictionary<string, (int, string)>(StringComparer.Ordinal)
        {
            ["src/Koan.Core/Hosting/Runtime/AppRuntime.cs"] =
                (2, "Diagnostics: how much of the startup banner to print."),
            ["src/Koan.Core/ServiceCollectionExtensions.cs"] =
                (2, "Diagnostics: default log level and the Koan log filter."),
            ["src/Koan.Observability/Infrastructure/ObservabilityPlan.cs"] =
                (2, "Diagnostics: sampling and exporter defaults."),
            ["src/Koan.Web/Controllers/HealthController.cs"] =
                (1, "Diagnostics: how much detail the health payload carries."),
            ["src/Koan.Mcp/Hosting/HttpSseTransport.cs"] =
                (1, "Transport-local heuristic: warns on plaintext in Production with no TLS-terminating "
                  + "proxy in front (IsProduction && !InContainer). One concept in one pillar."),
            ["src/Koan.Mcp/Hosting/StreamableHttpTransport.cs"] =
                (2, "Transport-local heuristic: the same plaintext warning, on two request paths."),
            ["src/Koan.Data.Core/Axes/DataAxisPreflight.cs"] =
                (1, "ARCH-0128 exception: a confirmed cross-tenant read is not a convenience, so nothing may "
                  + "unlock it. A KoanMagic gate would let Koan:AllowMagicInProduction unlock a data leak."),
            ["src/Koan.Web.Auth.Server/Keys/IssuerKeyGuard.cs"] =
                (2, "ARCH-0128 exception: guards Production AND Staging (both issue tokens real clients "
                  + "hold), with its own acknowledgement rather than the shared flag."),
            ["src/Koan.Web.Auth.Server/Keys/IssuerKeyRotationService.cs"] =
                (1, "ARCH-0128 exception: a background schedule with no work to do in Development. No "
                  + "surface is gated, so no gate applies."),
        };

    /// <summary>
    /// <c>KoanEnv</c> and its gate are the mechanism, not consumers of it. Counting their own declarations
    /// would make every snapshot edit a failure here, which teaches nothing.
    /// </summary>
    private static readonly HashSet<string> Mechanism =
        new(StringComparer.Ordinal) { "KoanEnv.cs", "KoanEnvGate.cs" };

    private static readonly Regex EnvironmentRead =
        new(@"\b(IsDevelopment|IsProduction|IsStaging)\b", RegexOptions.Compiled);

    // Trailing comments are not code. The negative lookbehind keeps "http://" in a string from truncating
    // the line before the part that matters.
    private static readonly Regex TrailingComment =
        new(@"(?<!:)//.*$", RegexOptions.Compiled);

    [Fact(DisplayName = "no capability reads the environment directly outside the enumerated exceptions")]
    public void Every_direct_environment_read_is_accounted_for()
    {
        var actual = ScanSource();

        var added = actual.Keys.Where(file => !Allowed.ContainsKey(file)).OrderBy(f => f, StringComparer.Ordinal).ToList();
        var removed = Allowed.Keys.Where(file => !actual.ContainsKey(file)).OrderBy(f => f, StringComparer.Ordinal).ToList();
        var changed = actual.Keys.Where(file => Allowed.TryGetValue(file, out var e) && e.Count != actual[file])
            .OrderBy(f => f, StringComparer.Ordinal).ToList();

        if (added.Count == 0 && removed.Count == 0 && changed.Count == 0) return;

        var report = new StringBuilder();
        report.AppendLine("Direct environment reads in src/ no longer match ARCH-0128's enumerated set.");
        report.AppendLine();

        if (added.Count > 0)
        {
            report.AppendLine("NEW direct read(s) — pick the named decision you are actually making:");
            report.AppendLine();
            report.AppendLine("  A convenience that is safe everywhere but risky in Production (auto-DDL,");
            report.AppendLine("  auto-discovery, a local key file):");
            report.AppendLine("      KoanEnv.Gate.Enforce(new KoanMagic(");
            report.AppendLine("          Capability: \"...\", Risk: \"...\", Remedy: \"...\",");
            report.AppendLine("          Consent: options.YourOptIn), environment, logger);");
            report.AppendLine("      Production is the gate, not Development — it runs in Staging, Test and CI.");
            report.AppendLine("      Use Gate.Announce instead when skipping is a coherent outcome.");
            report.AppendLine();
            report.AppendLine("  A surface that must NOT exist outside Development (admin UI, dev tokens,");
            report.AppendLine("  seeded credentials). No flag unlocks it, deliberately:");
            report.AppendLine("      if (!KoanEnv.Gate.DevelopmentOnly(environment)) return;");
            report.AppendLine();
            report.AppendLine("  An unconfigured default, never an authorization:");
            report.AppendLine("      options.Something ?? KoanEnv.Gate.LooksDeployed(environment)");
            report.AppendLine();
            report.AppendLine("  Diagnostics only (log level, banner, health detail) may read the environment");
            report.AppendLine("  directly — add the file to Allowed in this spec, with the reason.");
            report.AppendLine();
            foreach (var file in added) report.AppendLine($"    + {file} ({actual[file]} read(s))");
            report.AppendLine();
        }

        if (changed.Count > 0)
        {
            report.AppendLine("COUNT CHANGED in an allowed file — a new gate may have been added inside one:");
            foreach (var file in changed)
                report.AppendLine($"    ~ {file}: allowed {Allowed[file].Count}, found {actual[file]}  ({Allowed[file].Why})");
            report.AppendLine();
        }

        if (removed.Count > 0)
        {
            report.AppendLine("NO LONGER PRESENT — good news; delete the entry so the list stays true:");
            foreach (var file in removed) report.AppendLine($"    - {file}");
            report.AppendLine();
        }

        report.Append("See docs/decisions/ARCH-0128-environment-posture-is-a-named-decision.md.");
        Assert.Fail(report.ToString());
    }

    private static Dictionary<string, int> ScanSource()
    {
        var root = FindRepositoryRoot();
        var source = Path.Combine(root, "src");
        Directory.Exists(source).Should().BeTrue("the scan is meaningless without the source tree");

        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative.Contains("/obj/", StringComparison.Ordinal) ||
                relative.Contains("/bin/", StringComparison.Ordinal)) continue;
            if (Mechanism.Contains(Path.GetFileName(path))) continue;

            var count = File.ReadLines(path).Sum(CountReads);
            if (count > 0) found[relative] = count;
        }

        return found;
    }

    private static int CountReads(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
            trimmed.StartsWith("*", StringComparison.Ordinal) ||
            trimmed.StartsWith("/*", StringComparison.Ordinal)) return 0;

        return EnvironmentRead.Matches(TrailingComment.Replace(line, string.Empty)).Count;
    }

    // [CallerFilePath] is baked at compile time, which is the only reliable anchor: test output is
    // redirected outside the repository, so walking up from the assembly location finds nothing.
    private static string FindRepositoryRoot([CallerFilePath] string sourceFile = "")
    {
        foreach (var start in new[] { Path.GetDirectoryName(sourceFile)!, Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Koan.sln"))) return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Koan repository root.");
    }
}
