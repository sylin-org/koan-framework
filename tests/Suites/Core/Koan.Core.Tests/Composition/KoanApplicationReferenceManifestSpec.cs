using System.IO;
using System.Linq;
using System.Reflection;
using AwesomeAssertions;
using Koan.Core.Composition;
using Xunit;

[assembly: AssemblyMetadata("KoanSemanticActivationManifest", "1")]

namespace Koan.Core.Tests;

public sealed class KoanApplicationReferenceManifestSpec
{
    [Fact]
    public void Parses_versioned_references_and_preserves_raw_and_canonical_identities()
    {
        using var reader = new StringReader("""
            schema|1
            reference|project|Koan.Mcp|Sylin.Koan.Mcp
            reference|package|sylin.koan.app|Sylin.Koan.App
            reference|project|koan.mcp|Sylin.Koan.Mcp
            """);

        var manifest = KoanApplicationReferenceManifest.Parse(reader);

        manifest.IsPresent.Should().BeTrue();
        manifest.DirectReferences
            .Select(reference => $"{reference.Kind}|{reference.RawIdentity}|{reference.CanonicalIdentity}")
            .Should().Equal(
                "Package|sylin.koan.app|Sylin.Koan.App",
                "Project|Koan.Mcp|Sylin.Koan.Mcp");
        manifest.Contains(KoanReferenceKind.Package, "sylin.koan.app").Should().BeTrue();
        manifest.Contains(KoanReferenceKind.Project, "Koan.Communication").Should().BeFalse(
            "transitive modules are not direct application intent");
    }

    [Fact]
    public void Parses_project_references_whose_raw_identity_is_the_applications_own_name()
    {
        // PMC-062 evidence: the documented conformance-kit flow has a test project reference an
        // application project whose assembly carries an ordinary (non-Koan) name. The writer records
        // that assembly name as the raw identity — provenance — while the canonical identity stays
        // framework-owned. Rejecting the raw name made every real-host battery fail at boot.
        using var reader = new StringReader("""
            schema|1
            reference|package|Sylin.Koan.Testing|Sylin.Koan.Testing
            reference|project|TmplApp|Sylin.Koan.TmplApp
            dependency|Sylin.Koan.App|Sylin.Koan
            """);

        var manifest = KoanApplicationReferenceManifest.Parse(reader);

        manifest.IsPresent.Should().BeTrue();
        manifest.Contains(KoanReferenceKind.Project, "Sylin.Koan.TmplApp").Should().BeTrue();
    }

    [Fact]
    public void Parses_deduplicates_and_orders_dependencies()
    {
        using var reader = new StringReader("""
            schema|1
            dependency|Sylin.Koan.App|Sylin.Koan.Web
            dependency|Sylin.Koan|Sylin.Koan.Data.Core
            dependency|sylin.koan.app|sylin.koan.web
            dependency|Sylin.Koan|Sylin.Koan.Communication
            """);

        var manifest = KoanApplicationReferenceManifest.Parse(reader);

        manifest.Dependencies.Should().Equal(
            new KoanActivationDependency("Sylin.Koan", "Sylin.Koan.Communication"),
            new KoanActivationDependency("Sylin.Koan", "Sylin.Koan.Data.Core"),
            new KoanActivationDependency("Sylin.Koan.App", "Sylin.Koan.Web"));
    }

    [Fact]
    public void Missing_resource_is_unknown_while_schema_only_is_a_present_empty_declaration()
    {
        var unknown = KoanApplicationReferenceManifest.Load(typeof(KoanApplicationReferenceManifest).Assembly);
        using var reader = new StringReader("schema|1");
        var empty = KoanApplicationReferenceManifest.Parse(reader);

        unknown.IsPresent.Should().BeFalse();
        unknown.DirectReferences.Should().BeEmpty();
        unknown.Dependencies.Should().BeEmpty();
        empty.IsPresent.Should().BeTrue();
        empty.DirectReferences.Should().BeEmpty();
        empty.Dependencies.Should().BeEmpty();
    }

    [Fact]
    public void Missing_resource_on_a_build_marked_application_fails_with_a_corrective_rebuild_message()
    {
        var action = () => KoanApplicationReferenceManifest.Load(typeof(KoanApplicationReferenceManifestSpec).Assembly);

        action.Should().Throw<InvalidDataException>()
            .WithMessage("*promised*koan.references.manifest*Clean and rebuild*");
    }

    [Theory]
    [InlineData("schema|2")]
    [InlineData("schema|")]
    [InlineData("schema|1|extra")]
    [InlineData("reference|project|Koan.Mcp|Sylin.Koan.Mcp")]
    [InlineData("schema|1\nschema|1")]
    public void Malformed_schema_fails_with_corrective_guidance(string content)
    {
        using var reader = new StringReader(content);

        var action = () => KoanApplicationReferenceManifest.Parse(reader);

        action.Should().Throw<InvalidDataException>()
            .WithMessage("*koan.references.manifest*line *Rebuild the application*");
    }

    [Theory]
    [InlineData("reference|connector|Koan.Communication|Koan.Communication")]
    [InlineData("reference|package|Sylin.Koan.App")]
    [InlineData("reference|package|Example.NotKoan|Koan.App")]
    [InlineData("reference|project|Koan.Mcp|Example.NotKoan")]
    [InlineData("reference|project|Koan.Mcp|Koan.Mcp|extra")]
    public void Malformed_reference_fails_with_corrective_guidance(string record)
    {
        using var reader = new StringReader($"schema|1{Environment.NewLine}{record}");

        var action = () => KoanApplicationReferenceManifest.Parse(reader);

        action.Should().Throw<InvalidDataException>()
            .WithMessage("*koan.references.manifest*line 2*Rebuild the application*");
    }

    [Theory]
    [InlineData("dependency|Sylin.Koan.App")]
    [InlineData("dependency|Example.NotKoan|Koan.Web")]
    [InlineData("dependency|Sylin.Koan.App|Example.NotKoan")]
    [InlineData("dependency|Sylin.Koan.App|Sylin.Koan.Web|extra")]
    public void Malformed_dependency_fails_with_corrective_guidance(string record)
    {
        using var reader = new StringReader($"schema|1{Environment.NewLine}{record}");

        var action = () => KoanApplicationReferenceManifest.Parse(reader);

        action.Should().Throw<InvalidDataException>()
            .WithMessage("*koan.references.manifest*line 2*Rebuild the application*");
    }
}
