using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Koan.Samples.McpCodeMode.Tests;

public class UnionTypesSpec : IClassFixture<TestPipelineFixture>
{
    private readonly TestPipelineFixture _fx;
    public UnionTypesSpec(TestPipelineFixture fx) => _fx = fx;

    [Fact(DisplayName = ".d.ts should include union types for sets and relationships when metadata present")]
    public void Dts_ShouldContainUnionTypes()
    {
        // The generator writes to mcp-sdk/koan-code-mode.d.ts under test bin directory
        var baseDir = AppContext.BaseDirectory; // points to test bin
        var path = Path.Combine(baseDir, "mcp-sdk", "koan-code-mode.d.ts");
        File.Exists(path).Should().BeTrue($"Expected generated TypeScript definitions at {path}");
        var text = File.ReadAllText(path);

        // Assert set union: type TodoSet = "default" | "tenant-a" | "tenant-b";
        Regex.IsMatch(text, @"type\s+TodoSet\s*=\s*""default""\s*\|\s*""tenant-a""\s*\|\s*""tenant-b""")
            .Should().BeTrue("TodoSet union type missing or incorrect");

        // Assert relationship union (with all): type TodoRelationship = "assignedUser" | "tags" | "all";
        Regex.IsMatch(text, @"type\s+TodoRelationship\s*=\s*""assignedUser""\s*\|\s*""tags""\s*\|\s*""all""")
            .Should().BeTrue("TodoRelationship union type missing or incorrect");
    }
}
