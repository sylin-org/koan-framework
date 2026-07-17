using System.IO;
using AwesomeAssertions;
using Koan.Core.Composition;
using Koan.Core.Providers;
using Xunit;

namespace Koan.Core.Tests.Providers;

public sealed class ProviderCatalogSpec
{
    [Fact]
    public void Input_order_does_not_change_catalog_or_default_selection()
    {
        var alpha = new ProbeProvider("alpha", priority: 10);
        var beta = new ProbeProvider("beta", priority: 10);

        var forward = Compile(alpha, beta);
        var reverse = Compile(beta, alpha);

        forward.Candidates.Select(Canonical).Should().Equal(reverse.Candidates.Select(Canonical));
        forward.Best(forward.Candidates, ByPriority).Should().BeSameAs(alpha);
        reverse.Best(reverse.Candidates, ByPriority).Should().BeSameAs(alpha);
    }

    [Theory]
    [InlineData("", "beta", "Provider identities cannot be empty")]
    [InlineData("alpha", "ALPHA", "provider identity 'alpha'")]
    [InlineData("alpha|shared", "beta|SHARED", "provider alias 'shared'")]
    [InlineData("alpha|beta", "beta", "provider identity 'beta'")]
    public void Invalid_or_colliding_identity_rejects_before_catalog_publication(
        string first,
        string second,
        string expected)
    {
        var act = () => Compile(Parse(first), Parse(second));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{expected}*");
    }

    [Fact]
    public void Exact_lookup_returns_no_candidate_for_an_unknown_required_intent()
    {
        var alpha = new ProbeProvider("alpha", priority: 100);
        var catalog = Compile(alpha);

        catalog.Find("missing").Should().BeNull();
        catalog.Find("alpha").Should().BeSameAs(alpha);
        catalog.Find("a").Should().BeSameAs(alpha);
    }

    [Theory]
    [InlineData("package", "Sylin.Koan.Data.Connector.Alpha")]
    [InlineData("project", "Koan.Data.Connector.Alpha")]
    public void Package_and_project_reference_evidence_select_the_same_direct_candidate(
        string kind,
        string rawIdentity)
    {
        var alpha = new ProbeProvider(
            "alpha",
            references: ["Koan.Data.Connector.Alpha", "Sylin.Koan.Data.Connector.Alpha"]);
        var transitive = new ProbeProvider(
            "transitive",
            references: ["Koan.Data.Connector.Transitive", "Sylin.Koan.Data.Connector.Transitive"]);
        var catalog = Compile(alpha, transitive);

        catalog.Direct(Manifest(kind, rawIdentity)).Should().ContainSingle().Which.Should().BeSameAs(alpha);
    }

    [Fact]
    public void Pillar_comparer_wins_and_catalog_appends_only_the_stable_identity_tie()
    {
        var lowAssurance = new ProbeProvider("z-low", priority: 100, assurance: 1);
        var highAssuranceB = new ProbeProvider("b-high", priority: 1, assurance: 10);
        var highAssuranceA = new ProbeProvider("a-high", priority: 1, assurance: 10);
        var catalog = Compile(lowAssurance, highAssuranceB, highAssuranceA);

        catalog.Best(catalog.Candidates, ByAssuranceThenPriority).Should().BeSameAs(highAssuranceA);
    }

    [Fact]
    public void Catalogs_are_host_owned_and_do_not_share_candidate_state()
    {
        var first = Compile(new ProbeProvider("first"));
        var second = Compile(new ProbeProvider("second"));

        first.Find("first").Should().NotBeNull();
        first.Find("second").Should().BeNull();
        second.Find("second").Should().NotBeNull();
        second.Find("first").Should().BeNull();
    }

    [Fact]
    public void Description_and_priority_are_compiled_once_not_recomputed_by_lookup_or_selection()
    {
        var descriptions = 0;
        var provider = new ProbeProvider("alpha", priority: 42);
        var catalog = ProviderCatalog<ProbeProvider>.Compile(
            [provider],
            value =>
            {
                descriptions++;
                return Describe(value);
            });

        for (var index = 0; index < 10; index++)
        {
            catalog.Find("alpha").Should().BeSameAs(provider);
            catalog.Find("a").Should().BeSameAs(provider);
            catalog.Best(catalog.Candidates, ByPriority).Should().BeSameAs(provider);
        }

        descriptions.Should().Be(1);
        catalog.Describe(provider).Priority.Should().Be(42);
    }

    [Fact]
    public void Selection_receipt_normalizes_and_bounds_safe_evidence()
    {
        var receipt = new ProviderSelectionReceipt(
            " data:default ",
            " JSON ",
            ProviderIntentPosture.Automatic,
            priority: 0,
            reason: "built-in-floor",
            rejectedReasonCodes: ["capability-missing", "capability-missing", "not-direct"]);

        receipt.Subject.Should().Be("data:default");
        receipt.ProviderId.Should().Be("JSON");
        receipt.RejectedReasonCodes.Should().Equal("capability-missing", "not-direct");
    }

    [Theory]
    [InlineData("")]
    [InlineData("contains secret=value")]
    public void Selection_receipt_rejects_empty_or_nonsemantic_evidence(string reason)
    {
        var create = () => new ProviderSelectionReceipt(
            "data:default",
            "json",
            ProviderIntentPosture.Automatic,
            0,
            reason);

        create.Should().Throw<ArgumentException>();
    }

    private static ProviderCatalog<ProbeProvider> Compile(params ProbeProvider[] providers) =>
        ProviderCatalog<ProbeProvider>.Compile(providers, Describe);

    private static ProviderCandidateDescriptor Describe(ProbeProvider provider) => new(
        provider.Id,
        provider.Id == "alpha" ? provider.Aliases.Concat(["a"]).ToArray() : provider.Aliases,
        provider.References,
        provider.Priority);

    private static string Canonical(ProviderCandidate<ProbeProvider> candidate) =>
        $"{candidate.Id}|{string.Join(',', candidate.Aliases)}|{candidate.Priority}";

    private static int ByPriority(
        ProviderCandidate<ProbeProvider> left,
        ProviderCandidate<ProbeProvider> right) =>
        right.Priority.CompareTo(left.Priority);

    private static int ByAssuranceThenPriority(
        ProviderCandidate<ProbeProvider> left,
        ProviderCandidate<ProbeProvider> right)
    {
        var assurance = right.Value.Assurance.CompareTo(left.Value.Assurance);
        return assurance != 0 ? assurance : right.Priority.CompareTo(left.Priority);
    }

    private static ProbeProvider Parse(string value)
    {
        var parts = value.Split('|');
        return new ProbeProvider(parts[0], aliases: parts.Skip(1).ToArray());
    }

    private static KoanApplicationReferenceManifest Manifest(string kind, string rawIdentity)
    {
        var canonical = rawIdentity.StartsWith("Sylin.", StringComparison.Ordinal)
            ? rawIdentity
            : $"Sylin.{rawIdentity}";
        return KoanApplicationReferenceManifest.Parse(new StringReader(
            $"schema|1{Environment.NewLine}reference|{kind}|{rawIdentity}|{canonical}"));
    }

    private sealed class ProbeProvider
    {
        public ProbeProvider(
            string id,
            int priority = 0,
            int assurance = 0,
            IReadOnlyList<string>? aliases = null,
            IReadOnlyList<string>? references = null)
        {
            Id = id;
            Priority = priority;
            Assurance = assurance;
            Aliases = aliases ?? [];
            References = references ?? [];
        }

        public string Id { get; }
        public int Priority { get; }
        public int Assurance { get; }
        public IReadOnlyList<string> Aliases { get; }
        public IReadOnlyList<string> References { get; }
    }
}
