using System.IO;
using System.Text.RegularExpressions;

namespace Koan.Samples.McpCodeMode.Tests;

public class TypeScriptGenerationSpec : IClassFixture<TestPipelineFixture>
{
    private readonly TestPipelineFixture _fx;
    public TypeScriptGenerationSpec(TestPipelineFixture fx) => _fx = fx;

    [Fact(DisplayName = "Read-only entity omits mutation methods in d.ts")] 
    public void ReadOnlyEntity_ShouldOmitMutations()
    {
        // Locate generated definitions (default path per TypeScriptSdkOptions)
        var path = Path.GetFullPath("mcp-sdk/koan-code-mode.d.ts");
        File.Exists(path).Should().BeTrue("Expected TypeScript definitions file to exist at default path.");
        var content = File.ReadAllText(path);
        // Find AuditLog block and ensure no upsert/delete signatures
        var auditSection = Regex.Match(content, @"interface IAuditLogOperations \{[^}]+\}", RegexOptions.Multiline);
        auditSection.Success.Should().BeTrue("Expected IAuditLogOperations interface.");
        var sectionText = auditSection.Value;
        sectionText.Contains("upsert(").Should().BeFalse("Read-only entity should not expose upsert");
        sectionText.Contains("delete(").Should().BeFalse("Read-only entity should not expose delete");
        sectionText.Contains("deleteMany(").Should().BeFalse("Read-only entity should not expose deleteMany");
    }
}
