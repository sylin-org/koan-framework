using System.IO;
using System.Linq;
using AwesomeAssertions;
using Koan.Core.Composition;
using Xunit;

namespace Koan.Core.Tests;

public sealed class KoanApplicationReferenceManifestSpec
{
    [Fact]
    public void Parses_deduplicates_and_orders_direct_reference_intent()
    {
        using var reader = new StringReader("""
            project|Koan.Mcp
            package|Sylin.Koan.App
            project|Koan.Data.Connector.Sqlite
            project|koan.mcp
            """);

        var manifest = KoanApplicationReferenceManifest.Parse(reader);

        manifest.IsPresent.Should().BeTrue();
        manifest.DirectReferences.Select(reference => $"{reference.Kind}|{reference.Identity}").Should().Equal(
            "Package|Sylin.Koan.App",
            "Project|Koan.Data.Connector.Sqlite",
            "Project|Koan.Mcp");
        manifest.Contains(KoanReferenceKind.Package, "sylin.koan.app").Should().BeTrue();
        manifest.Contains(KoanReferenceKind.Project, "Koan.Communication").Should().BeFalse(
            "transitive modules are not direct application intent");
    }

    [Fact]
    public void Missing_resource_is_unknown_not_an_empty_declaration()
    {
        var manifest = KoanApplicationReferenceManifest.Load(typeof(KoanApplicationReferenceManifest).Assembly);

        manifest.IsPresent.Should().BeFalse();
        manifest.DirectReferences.Should().BeEmpty();
    }

    [Theory]
    [InlineData("connector|Koan.Communication")]
    [InlineData("package|")]
    [InlineData("package|Example.NotKoan")]
    [InlineData("project|Koan.Mcp|extra")]
    public void Present_malformed_resource_fails_with_corrective_guidance(string line)
    {
        using var reader = new StringReader(line);

        var action = () => KoanApplicationReferenceManifest.Parse(reader);

        action.Should().Throw<InvalidDataException>()
            .WithMessage("*koan.references.manifest*line 1*Rebuild the application*");
    }
}
