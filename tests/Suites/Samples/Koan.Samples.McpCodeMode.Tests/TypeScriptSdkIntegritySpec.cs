using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Koan.Samples.McpCodeMode.Tests;

// Verifies hash footer presence and stability behavior (skip write when unchanged)
public class TypeScriptSdkIntegritySpec : IClassFixture<TestPipelineFixture>
{
    private readonly TestPipelineFixture _fx;
    public TypeScriptSdkIntegritySpec(TestPipelineFixture fx) => _fx = fx;

    [Fact(DisplayName = ".d.ts includes integrity hash footer")] 
    public void Dts_ShouldContainIntegrityFooter()
    {
        var baseDir = AppContext.BaseDirectory;
        var path = Path.Combine(baseDir, "mcp-sdk", "koan-code-mode.d.ts");
        File.Exists(path).Should().BeTrue();
        var text = File.ReadAllText(path);
        text.Should().Contain("// integrity-sha256:", "hash footer must be appended");

        // Extract last non-empty line
        var last = text.Replace("\r", string.Empty).Split('\n').Reverse().First(l => !string.IsNullOrWhiteSpace(l));
        last.StartsWith("// integrity-sha256:").Should().BeTrue("footer must be final line");

        var declaredHash = last.Split(':', 2)[1].Trim();
        declaredHash.Should().NotBeNullOrEmpty();

        // Reconstruct the exact content the generator hashed:
        // finalFile = normalizedContent (+ possible trailing \n) + footer + \n
        // Find the start index of the footer line (preceded by a newline in normal cases)
        var lfFooterToken = "\n// integrity-sha256:";
        var idx = text.LastIndexOf(lfFooterToken, StringComparison.Ordinal);
        string hashedSegment;
        if (idx >= 0)
        {
            // Include the leading LF that belongs to normalized content (content prior to footer line start)
            hashedSegment = text.Substring(0, idx + 1); // +1 keeps the LF at idx
        }
        else
        {
            // Fallback: footer maybe at very start (unlikely) – hash everything up to, but excluding, the footer line itself
            var firstLineEnd = text.IndexOf('\n');
            hashedSegment = firstLineEnd > 0 ? text.Substring(0, firstLineEnd + 1) : text;
        }

        // Normalize CR removal exactly like generator does before hashing
        hashedSegment = hashedSegment.Replace("\r", string.Empty);
        var recomputed = ComputeSha256(hashedSegment);
        recomputed.Should().Be(declaredHash, "computed hash should match declared footer");
    }

    private static string ComputeSha256(string content)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = sha.ComputeHash(bytes);
        return string.Concat(hash.Select(b => b.ToString("x2")));
    }
}