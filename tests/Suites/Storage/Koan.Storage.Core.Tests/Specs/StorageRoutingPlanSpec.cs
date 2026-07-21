using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Capabilities;
using Koan.Storage.Abstractions;
using Koan.Storage.Abstractions.Capabilities;
using Koan.Storage.Options;
using Koan.Storage.Replication;
using Koan.Storage.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Storage.Core.Tests.Specs;

public sealed class StorageRoutingPlanSpec
{
    [Fact]
    public void No_profiles_fail_with_a_correction()
    {
        var act = () => Compile(new StorageOptions(), new LocalProvider());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no profiles*Configure at least one*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Omitted_provider_elects_the_available_placement(string? provider)
    {
        using var plan = Compile(Profile(provider: provider), new LocalProvider());

        var resolved = plan.Resolve("", "");
        resolved.Route.Provider.Name.Should().Be("local-low");
        resolved.Route.Receipt.Reason.Should().Be("automatic-local");
    }

    [Fact]
    public void Missing_container_fails_during_plan_compilation()
    {
        var act = () => Compile(Profile(container: ""), new LocalProvider());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*profile 'main' has no container*");
    }

    [Fact]
    public void Unknown_explicit_provider_names_the_available_correction()
    {
        var act = () => Compile(Profile(provider: "filesystem"), new LocalProvider());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*unknown provider 'filesystem'*local-low*");
    }

    [Fact]
    public void Explicit_provider_pin_wins_over_priority()
    {
        using var plan = Compile(Profile(provider: "local-low"), new LocalProvider(), new PreferredLocalProvider());

        plan.Resolve("main", "").Route.Provider.Name.Should().Be("local-low");
        plan.Routes.Single().Receipt.Intent.Should().Be(Koan.Core.Providers.ProviderIntentPosture.Required);
    }

    [Fact]
    public void Automatic_election_uses_priority_then_stable_identity()
    {
        using var plan = Compile(Profile(), new LocalProvider(), new PreferredLocalProvider());

        plan.Resolve("main", "").Route.Provider.Name.Should().Be("local-high");
    }

    [Fact]
    public void Invalid_default_names_the_valid_choices()
    {
        var options = Profile();
        options.DefaultProfile = "missing";

        var act = () => Compile(options, new LocalProvider());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*DefaultProfile 'missing'*main*");
    }

    [Fact]
    public void Several_profiles_can_boot_but_implicit_use_requires_a_default()
    {
        var options = Profile();
        options.Profiles.Add("archive", new StorageOptions.StorageProfile { Container = "archive" });
        using var plan = Compile(options, new LocalProvider());

        plan.DefaultProfile.Should().BeNull();
        var act = () => plan.Resolve("", "");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*several profiles*DefaultProfile*explicit profile*");
        plan.Resolve("archive", "").Container.Should().Be("archive");
    }

    [Fact]
    public void Sole_profile_is_the_implicit_default_without_an_extra_mode_knob()
    {
        using var plan = Compile(Profile(), new LocalProvider());

        plan.DefaultProfile.Should().Be("main");
        plan.Resolve("", "override").Container.Should().Be("override");
    }

    [Fact]
    public void Required_replication_does_not_degrade_to_one_provider()
    {
        var options = Profile(mode: StorageMode.Replicated);

        var act = () => Compile(options, new LocalProvider());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*requires Replicated mode*no Remote provider*will not weaken*");
    }

    [Fact]
    public void Contradictory_exact_provider_and_mode_fail()
    {
        var options = Profile(provider: "remote", mode: StorageMode.Local);

        var act = () => Compile(options, new RemoteProvider());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*pins provider 'remote' (Remote)*requires Local mode*");
    }

    [Fact]
    public void Local_and_remote_providers_compile_one_replicated_route()
    {
        using var plan = Compile(Profile(), new LocalProvider(), new RemoteProvider());

        var route = plan.Routes.Single();
        route.Provider.Placement.Should().Be(StorageProviderPlacement.Composite);
        route.Receipt.Reason.Should().Be("automatic-replicated");
        route.Capabilities.Has(StorageCaps.SequentialRead).Should().BeTrue();
    }

    [Fact]
    public void Required_replication_preserves_required_intent()
    {
        using var plan = Compile(
            Profile(mode: StorageMode.Replicated),
            new LocalProvider(),
            new RemoteProvider());

        var route = plan.Routes.Single();
        route.Receipt.Intent.Should().Be(Koan.Core.Providers.ProviderIntentPosture.Required);
        route.Receipt.Reason.Should().Be("required-replicated");
    }

    [Fact]
    public void Explicit_composite_provider_is_used_without_recomposition()
    {
        var composite = new CompositeProvider();
        using var plan = Compile(
            Profile(provider: composite.Name, mode: StorageMode.Replicated),
            composite,
            new LocalProvider(),
            new RemoteProvider());

        var route = plan.Routes.Single();
        route.Provider.Should().BeSameAs(composite);
        route.Receipt.Intent.Should().Be(Koan.Core.Providers.ProviderIntentPosture.Required);
        route.Receipt.Reason.Should().Be("explicit-composite");
    }

    [Fact]
    public void Capability_claims_and_optional_interfaces_must_agree()
    {
        var act = () => new StorageProviderCatalog([new FalsePresignProvider()]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*inconsistent capability 'storage.presign.read'*");
    }

    private static StorageRoutingPlan Compile(StorageOptions options, params IStorageProvider[] providers)
        => new(
            Microsoft.Extensions.Options.Options.Create(options),
            new StorageProviderCatalog(providers),
            NullLogger<StorageRoutingPlan>.Instance);

    private static StorageOptions Profile(
        string? provider = null,
        string container = "files",
        StorageMode? mode = null)
        => new()
        {
            Profiles = new Dictionary<string, StorageOptions.StorageProfile>(StringComparer.OrdinalIgnoreCase)
            {
                ["main"] = new()
                {
                    Provider = provider,
                    Container = container,
                    Mode = mode
                }
            }
        };

    private abstract class Provider(string name, StorageProviderPlacement placement) : IStorageProvider
    {
        public string Name { get; } = name;
        public StorageProviderPlacement Placement { get; } = placement;
        public virtual void Describe(ICapabilities caps) => caps.Add(StorageCaps.SequentialRead);
        public Task Write(string container, string key, Stream content, string? contentType, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Stream> OpenRead(string container, string key, CancellationToken ct = default) => Task.FromResult<Stream>(new MemoryStream());
        public Task<(Stream Stream, long? Length)> OpenReadRange(string container, string key, long? from, long? to, CancellationToken ct = default) => Task.FromResult<(Stream, long?)>((new MemoryStream(), 0));
        public Task<bool> Delete(string container, string key, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> Exists(string container, string key, CancellationToken ct = default) => Task.FromResult(true);
    }

    [ProviderPriority(0)]
    private sealed class LocalProvider() : Provider("local-low", StorageProviderPlacement.Local);

    [ProviderPriority(100)]
    private sealed class PreferredLocalProvider() : Provider("local-high", StorageProviderPlacement.Local);

    private sealed class RemoteProvider() : Provider("remote", StorageProviderPlacement.Remote);

    private sealed class CompositeProvider() : Provider("composite", StorageProviderPlacement.Composite);

    private sealed class FalsePresignProvider() : Provider("false-presign", StorageProviderPlacement.Remote)
    {
        public override void Describe(ICapabilities caps)
            => base.Describe(caps.Add(StorageCaps.PresignedRead));
    }
}
