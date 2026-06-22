using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Data.Abstractions.Capabilities;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Core.Model;
using Koan.Data.Vector;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Pipeline;

/// <summary>
/// DATA-0105 §3.3 / ARCH-0095 — the vector plane bypasses the RepositoryFacade chokepoint, and the schemaless
/// vector metadata-filter splitter never resolves a managed field, so a KNN over a managed-field-scoped entity
/// would return results across scopes. It must fail closed under an active managed scope. Validated with a
/// generic descriptor (no tenant). The check runs BEFORE the vector adapter is resolved, so no host is needed.
/// </summary>
[Collection("managed-field-registry")]
public sealed class ManagedFieldVectorFailClosedSpec : IDisposable
{
    private static readonly AsyncLocal<string?> _scope = new();

    public ManagedFieldVectorFailClosedSpec() => ManagedFieldRegistry.Reset();
    public void Dispose() { _scope.Value = null; ManagedFieldRegistry.Reset(); }

    private sealed class Embedded : Entity<Embedded> { public string Text { get; set; } = ""; }

    private static void Register() => ManagedFieldRegistry.Register(new ManagedFieldDescriptor(
        StorageName: "__vscope",
        ClrType: typeof(string),
        ValueProvider: () => _scope.Value,
        AppliesTo: t => t == typeof(Embedded),
        RequiredCapability: DataCaps.Isolation.RowScoped));

    [Fact]
    public async Task Vector_search_fails_closed_under_an_active_managed_scope()
    {
        Register();
        _scope.Value = "acme";

        var act = async () => await Vector<Embedded>.Search(new float[] { 0.1f, 0.2f });

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*fails closed*");
    }

    [Fact]
    public async Task Vector_search_does_not_fail_closed_when_unscoped()
    {
        Register();
        // No active managed scope ⇒ the fail-closed does NOT fire; it proceeds to adapter resolution and errors
        // for a different reason (no vector adapter / host). The point: it is not the managed fail-closed.
        var act = async () => await Vector<Embedded>.Search(new float[] { 0.1f, 0.2f });

        (await act.Should().ThrowAsync<Exception>()).Which.Should().NotBeOfType<NotSupportedException>();
    }
}
