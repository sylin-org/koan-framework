using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Koan.Samples.McpCodeMode.Tests;

// Compares the sanitized (footer-stripped) generated .d.ts with a checked-in baseline to detect drift.
public class TypeScriptSdkSnapshotSpec : IClassFixture<TestPipelineFixture>
{
    private readonly TestPipelineFixture _fx;
    public TypeScriptSdkSnapshotSpec(TestPipelineFixture fx) => _fx = fx;

    private static string Sanitize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        // Normalize line endings + strip footer line if present
        var cleaned = raw.Replace("\r", string.Empty);
        var lines = cleaned.Split('\n');
        if (lines.Length > 0 && lines[^1].TrimStart().StartsWith("// integrity-sha256:"))
        {
            lines = lines.Take(lines.Length - 1).ToArray();
        }
        // Remove any trailing blank lines for deterministic comparison
        while (lines.Length > 0 && string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines = lines.Take(lines.Length - 1).ToArray();
        }
        return string.Join('\n', lines) + '\n';
    }

    [Fact(DisplayName = "Generated .d.ts matches baseline snapshot (excluding footer)")]
    public void Generated_Matches_Baseline()
    {
        var baseDir = AppContext.BaseDirectory;
        var genPath = Path.Combine(baseDir, "mcp-sdk", "koan-code-mode.d.ts");
        File.Exists(genPath).Should().BeTrue();
        var generatedRaw = File.ReadAllText(genPath, Encoding.UTF8);
        var generated = Sanitize(generatedRaw);

        var snapshotDir = Path.Combine(baseDir, "_snapshots");
        if (!Directory.Exists(snapshotDir)) Directory.CreateDirectory(snapshotDir);
        var baselinePath = Path.Combine(snapshotDir, "koan-code-mode.d.ts.baseline");
        if (!File.Exists(baselinePath))
        {
            File.WriteAllText(baselinePath, generated, Encoding.UTF8);
            return; // First-run initialization; treat as pass.
        }
        var baselineRaw = File.ReadAllText(baselinePath, Encoding.UTF8);
        var baseline = Sanitize(baselineRaw);

        if (IsPlaceholder(baseline))
        {
            // Upgrade placeholder with actual generated snapshot silently
            File.WriteAllText(baselinePath, generated, Encoding.UTF8);
            return; // Do not fail; baseline established.
        }

        if (!string.Equals(generated, baseline, StringComparison.Ordinal))
        {
            var diff = InlineDiff(baseline, generated);
            Assert.Fail($"Generated TypeScript SDK drifted from baseline. Diff (baseline -> generated):\n{diff}");
        }
    }

    private static bool IsPlaceholder(string text) => text.Contains("Minimal placeholder", StringComparison.OrdinalIgnoreCase);

    private static string InlineDiff(string expected, string actual)
    {
        var expLines = expected.Split('\n');
        var actLines = actual.Split('\n');
        var max = Math.Max(expLines.Length, actLines.Length);
        var sb = new StringBuilder();
        for (int i = 0; i < max; i++)
        {
            var e = i < expLines.Length ? expLines[i] : string.Empty;
            var a = i < actLines.Length ? actLines[i] : string.Empty;
            if (!string.Equals(e, a, StringComparison.Ordinal))
            {
                sb.AppendLine($"@@ line {i + 1} @@");
                sb.AppendLine($"- {e}");
                sb.AppendLine($"+ {a}");
            }
        }
        if (sb.Length == 0) sb.AppendLine("<no diff>");
        return sb.ToString();
    }
}
