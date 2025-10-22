using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Koan.Samples.McpCodeMode.Tests;

public class UnionTypesNegativeSpec : IClassFixture<TestPipelineFixture>
{
    private readonly TestPipelineFixture _fx;
    public UnionTypesNegativeSpec(TestPipelineFixture fx) => _fx = fx;

    [Fact(DisplayName = ".d.ts should NOT include union types for non-enriched entity (AuditLog)")]
    public void Dts_ShouldOmitUnionTypes_ForNonEnrichedEntity()
    {
        var baseDir = AppContext.BaseDirectory;
        var path = Path.Combine(baseDir, "mcp-sdk", "koan-code-mode.d.ts");
        File.Exists(path).Should().BeTrue();
        var text = File.ReadAllText(path);

        // Assert no AuditLogSet or AuditLogRelationship union types declared
        Regex.IsMatch(text, @"type\s+AuditLogSet\s*=").Should().BeFalse("AuditLogSet union should not be generated without metadata");
        Regex.IsMatch(text, @"type\s+AuditLogRelationship\s*=").Should().BeFalse("AuditLogRelationship union should not be generated without metadata");

        // Ensure at least one operation signature for AuditLog uses plain string for set param (no AuditLogSet reference)
        // Use single-line (DOTALL) via (?s) so method signatures spanning lines are matched
        Regex.IsMatch(text, @"(?s)AuditLog[\s\S]*?set\?: string").Should().BeTrue("AuditLog operations should use plain string (not union) for set param");

        // And ensure no accidental union form made it in for AuditLog params
        Regex.IsMatch(text, @"AuditLog[\s\S]*set\?:\s+AuditLogSet\s*\|").Should().BeFalse("AuditLog set param should not reference AuditLogSet union");
    }
}