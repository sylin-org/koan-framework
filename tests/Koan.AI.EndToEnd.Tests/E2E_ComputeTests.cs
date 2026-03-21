using FluentAssertions;
using Koan.AI.Compute;
using Koan.AI.Contracts.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.AI.EndToEnd.Tests;

/// <summary>
/// End-to-end tests verifying Compute.* static facade through the full
/// DI-bootstrapped framework: facade -> IComputeService -> local GPU detection.
/// </summary>
[Trait("Category", "EndToEnd")]
[Collection("EndToEnd")]
public sealed class E2E_ComputeTests : IDisposable
{
    private readonly KoanTestFixture _fixture;

    public E2E_ComputeTests()
    {
        _fixture = new KoanTestFixture();
    }

    [Fact]
    public void ComputeService_IsResolvableFromDI()
    {
        var service = _fixture.Services.GetService<IComputeService>();
        service.Should().NotBeNull("IComputeService should be registered by Koan.AI.Compute auto-registrar");
    }

    [Fact]
    public async Task Compute_Available_ReturnsLocalResource()
    {
        var resource = await Koan.AI.Compute.Compute.Available();

        resource.Should().NotBeNull();
        resource!.Location.Should().Be(ComputeLocation.Local);
        resource.Id.Should().Be("local");
        resource.Status.Should().Be(ComputeStatus.Available);
    }

    [Fact]
    public async Task Compute_Fleet_ContainsLocalResource()
    {
        var fleet = await Koan.AI.Compute.Compute.Fleet();

        fleet.Should().NotBeEmpty();
        fleet.Should().Contain(r => r.Location == ComputeLocation.Local);
    }

    [Fact]
    public async Task Compute_Resolve_WithMinimalRequirement_ReturnsTarget()
    {
        var requirement = Koan.AI.Compute.Compute.Require();
        var resolution = await Koan.AI.Compute.Compute.Resolve(requirement);

        resolution.Should().NotBeNull();
        resolution.Target.Should().NotBeNull();
        resolution.Target.Location.Should().Be(ComputeLocation.Local);
    }

    [Fact]
    public async Task Compute_Check_LocalInference_Succeeds()
    {
        var spec = new ReadinessSpec
        {
            NetworkRequired = false,
            RequiredCapabilities = [ComputeCapability.Inference],
            RequiredModels = []
        };

        var ready = await Koan.AI.Compute.Compute.Check(spec);

        ready.Should().BeTrue();
    }

    [Fact]
    public async Task Compute_Check_NetworkRequired_ReturnsFalse()
    {
        var spec = new ReadinessSpec
        {
            NetworkRequired = true,
            RequiredCapabilities = [],
            RequiredModels = []
        };

        var ready = await Koan.AI.Compute.Compute.Check(spec);

        ready.Should().BeFalse("network compute is not yet implemented");
    }

    public void Dispose() => _fixture.Dispose();
}
