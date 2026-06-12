using AwesomeAssertions;
using Koan.AI.Contracts.Routing;
using Koan.AI.Resolution;
using Koan.Core.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.AI.EndToEnd.Tests;

/// <summary>
/// End-to-end tests verifying that the full DI pipeline resolves adapters correctly.
/// Framework is booted via AddKoan() with real auto-registrars.
/// </summary>
[Trait("Category", "EndToEnd")]
[Collection("EndToEnd")]
public sealed class E2E_AdapterResolutionTests : IDisposable
{
    private readonly KoanTestFixture _fixture;

    public E2E_AdapterResolutionTests()
    {
        _fixture = new KoanTestFixture();
    }

    [Fact]
    public void Framework_Boots_WithoutErrors()
    {
        _fixture.Services.Should().NotBeNull();

        var registry = _fixture.Services.GetService<IAiAdapterRegistry>();
        registry.Should().NotBeNull("IAiAdapterRegistry should be resolvable from the DI container");
    }

    [Fact]
    public void RegisteredAdapter_ResolvableViaAdapterResolver()
    {
        _fixture.RegisterAdapter("test-ollama", AiCapability.Chat, AiCapability.Pull);

        var result = AdapterResolver.Resolve(_fixture.AdapterRegistry, AiCapability.Pull);

        result.Id.Should().Be("test-ollama");
    }

    [Fact]
    public void SinglePullAdapter_ResolvesViaAdapterResolver()
    {
        _fixture.RegisterAdapter("test-ollama", AiCapability.Pull, AiCapability.Chat);

        var pullAdapter = AdapterResolver.Resolve(_fixture.AdapterRegistry, AiCapability.Pull);

        pullAdapter.Should().NotBeNull();
        pullAdapter.Id.Should().Be("test-ollama");
        pullAdapter.HasCapability(AiCapability.Pull).Should().BeTrue();
        pullAdapter.ModelManager.Should().NotBeNull("adapter with Pull capability should have a ModelManager");
    }

    [Fact]
    public void MultipleAdapters_Ambiguous_ThrowsAmbiguousException()
    {
        _fixture.RegisterAdapter("adapter-a", AiCapability.Pull);
        _fixture.RegisterAdapter("adapter-b", AiCapability.Pull);

        var act = () => AdapterResolver.Resolve(_fixture.AdapterRegistry, AiCapability.Pull);

        act.Should().Throw<AmbiguousAdapterException>()
            .Which.AdapterIds.Should().Contain("adapter-a").And.Contain("adapter-b");
    }

    [Fact]
    public void MultipleAdapters_ExplicitTarget_Resolves()
    {
        _fixture.RegisterAdapter("adapter-a", AiCapability.Pull);
        _fixture.RegisterAdapter("adapter-b", AiCapability.Pull);

        var result = AdapterResolver.Resolve(_fixture.AdapterRegistry, AiCapability.Pull, target: "adapter-a");

        result.Id.Should().Be("adapter-a");
    }

    [Fact]
    public void NoAdapters_WithCapability_ThrowsClearError()
    {
        var act = () => AdapterResolver.Resolve(_fixture.AdapterRegistry, AiCapability.Train);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No adapter*Train*");
    }

    [Fact]
    public void AdapterRegistry_AddAndGet_RoundTrips()
    {
        _fixture.RegisterAdapter("roundtrip-adapter", AiCapability.Chat, AiCapability.Embed);

        var retrieved = _fixture.AdapterRegistry.Get("roundtrip-adapter");

        retrieved.Should().NotBeNull();
        retrieved!.Capabilities.Should().Contain(AiCapability.Chat);
        retrieved.Capabilities.Should().Contain(AiCapability.Embed);
    }

    public void Dispose() => _fixture.Dispose();
}
