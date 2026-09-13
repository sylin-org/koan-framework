using Koan.Core;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.Identity.Erasure;
using Koan.Identity.Roles;
using Xunit;

namespace Koan.Identity.Tests;

/// <summary>
/// A single shared, OFFLINE host (in-memory data adapter) for the SEC-0007 P0 / Layer-0 acceptance, in the
/// neutral <c>"Test"</c> environment (non-production so the data adapter is permissive, non-Development so the
/// full Koan service graph — incl. ASP.NET's web-only MVC services from the auth pillar — is not strict-validated
/// against a generic host). A single shared host keeps every fact on the same store. <c>Koan.Tenancy</c> is
/// referenced live (Closed posture) so the facts exercise that
/// identity entities are ambient-exempt (the global plane) and the <c>Membership</c> soft-FK resolves.
/// </summary>
public sealed class IdentityHostFixture : IAsyncLifetime
{
    /// <summary>The dev person id used by the dev-seed fact (matches the tenancy dev membership so they reconcile).</summary>
    public const string DevUser = "devboss";

    private IntegrationHost? _host;
    public TestIdentityActorAccessor Actor { get; } = new();

    public IServiceProvider Services =>
        _host?.Services ?? throw new InvalidOperationException("Host not started.");

    public async ValueTask InitializeAsync()
    {
        _host = await KoanIntegrationHost.Configure()
            .ConfigureServices(s =>
            {
                s.TryAddEnumerable(ServiceDescriptor.Scoped<IIdentityErasureContributor, TestIdentityErasureContributor>());
                s.TryAddEnumerable(ServiceDescriptor.Singleton<IScopedRoleCatalogContributor, TestScopedRoleCatalog>());
                s.TryAddEnumerable(ServiceDescriptor.Scoped<IScopedRoleAuthorityContributor, TestScopedRoleAuthority>());
                s.TryAddEnumerable(ServiceDescriptor.Scoped<IScopedRoleGuardContributor, TestScopedRoleGuard>());
                s.TryAddSingleton<IScopedRoleSubjectAccessor>(Actor);
                s.AddKoan();
            })
            .StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
    }
}

public sealed class TestIdentityActorAccessor : IScopedRoleSubjectAccessor
{
    private readonly AsyncLocal<string?> _subject = new();
    public string? CurrentSubject => _subject.Value;
    public IDisposable Use(string? subject)
    {
        var prior = _subject.Value;
        _subject.Value = subject;
        return new Restore(() => _subject.Value = prior);
    }
    private sealed class Restore(Action restore) : IDisposable
    {
        private bool _done;
        public void Dispose() { if (!_done) { _done = true; restore(); } }
    }
}

internal sealed class TestScopedRoleCatalog : IScopedRoleCatalogContributor
{
    public void Describe(ScopedRoleCatalogBuilder catalog)
    {
        catalog.Scope("space");
        catalog.Scope("topic", "space");
        catalog.Scope("folder");
        catalog.Scope("document", "folder");
        catalog.Capability("discussion.read", ["space", "topic"], allowsAnonymous: true);
        catalog.Capability("discussion.reply", ["space", "topic"]);
        catalog.Capability("discussion.approve", ["space", "topic"], parameters: ["amount", "department"]);
        catalog.Capability("docs.read", ["folder", "document"]);
        catalog.Resource<ScopedDiscussionPost>("topic", post => post.TenantId, post => post.TopicId)
            .Read("discussion.read")
            .Create("discussion.reply");
    }
}

internal sealed class TestScopedRoleAuthority : IScopedRoleAuthorityContributor
{
    private static readonly IReadOnlySet<ScopedRoleAuthorityOperation> Operations =
        Enum.GetValues<ScopedRoleAuthorityOperation>().ToHashSet();

    public ValueTask<IReadOnlyList<ScopedRoleAuthorityEnvelope>> Contribute(
        ScopedRoleAuthorityRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!request.Actor.Subject.StartsWith("owner:", StringComparison.Ordinal))
            return ValueTask.FromResult<IReadOnlyList<ScopedRoleAuthorityEnvelope>>([]);
        return ValueTask.FromResult<IReadOnlyList<ScopedRoleAuthorityEnvelope>>([
            new(request.Target, Operations, Descendants: true, AllowSelfMembership: true,
                ProofKey: "test-owner", ProofVersion: 1)
        ]);
    }

    public ValueTask<bool> Validate(ScopedRoleAuthorityRequest request,
        ScopedRoleAuthorityEnvelope envelope, CancellationToken ct = default)
        => ValueTask.FromResult(envelope.ProofKey == "test-owner" && envelope.ProofVersion == 1);
}

internal sealed class TestScopedRoleGuard : IScopedRoleGuardContributor
{
    public ValueTask<ScopedRoleGuardResult> Evaluate(ScopedRoleGuardRequest request, CancellationToken ct = default)
        => ValueTask.FromResult(request.Parameters.TryGetValue("blocked", out var value) && value is true
            ? ScopedRoleGuardResult.Deny("guard.blocked", "The application guard blocks this action.", "test", 1)
            : ScopedRoleGuardResult.Permit());
}
