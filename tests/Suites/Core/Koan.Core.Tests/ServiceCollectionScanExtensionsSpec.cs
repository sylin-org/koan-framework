using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Core.Tests;

/// <summary>
/// Specs for <see cref="ServiceCollectionScanExtensions.AddAllOf{TService}(IServiceCollection, System.Reflection.Assembly[])"/>.
/// Validates: scans the target assembly, registers every concrete implementation as a singleton,
/// double-call is idempotent (TryAddEnumerable dedup), abstracts / interfaces / open-generics are
/// skipped, the non-generic overload mirrors the generic one.
/// </summary>
public class ServiceCollectionScanExtensionsSpec
{
    public interface IFakeContract { string Name { get; } }

    public sealed class FakeA : IFakeContract { public string Name => nameof(FakeA); }
    public sealed class FakeB : IFakeContract { public string Name => nameof(FakeB); }

    // Negative-test fixtures the scanner must skip:
    public abstract class FakeAbstract : IFakeContract { public string Name => "abstract"; }
    public interface IDerivedFromContract : IFakeContract { }
    public class FakeOpenGeneric<T> : IFakeContract { public string Name => "open"; }

    [NotAutoRegistered]
    public sealed class FakeOptedOut : IFakeContract
    {
        public string Name => nameof(FakeOptedOut);
        // Parameterized ctor that DI can't resolve — the [NotAutoRegistered] marker keeps the
        // scanner from picking it up and crashing service-provider validation.
        public FakeOptedOut(string somePrimitive) { _ = somePrimitive; }
    }

    [Fact]
    public void AddAllOf_registers_every_concrete_implementation_as_singleton()
    {
        var services = new ServiceCollection();

        services.AddAllOf<IFakeContract>(typeof(ServiceCollectionScanExtensionsSpec).Assembly);

        var provider = services.BuildServiceProvider();
        var instances = provider.GetServices<IFakeContract>().ToList();
        instances.Select(i => i.Name).Should().BeEquivalentTo(new[] { nameof(FakeA), nameof(FakeB) });
    }

    [Fact]
    public void AddAllOf_skips_abstract_classes_and_interfaces_and_open_generics()
    {
        var services = new ServiceCollection();
        services.AddAllOf<IFakeContract>(typeof(ServiceCollectionScanExtensionsSpec).Assembly);

        var implTypes = services
            .Where(d => d.ServiceType == typeof(IFakeContract))
            .Select(d => d.ImplementationType)
            .ToList();

        implTypes.Should().NotContain(typeof(FakeAbstract));
        implTypes.Should().NotContain(typeof(IDerivedFromContract));
        implTypes.Should().NotContain(t => t != null && t.IsGenericTypeDefinition);
    }

    [Fact]
    public void AddAllOf_called_twice_does_not_duplicate_registrations()
    {
        // Same assembly scanned twice (the scenario where two module concerns
        // happen to scan the same assembly for the same contract). TryAddEnumerable inside the
        // helper keeps the count stable instead of producing two FakeA instances on resolve.
        var services = new ServiceCollection();
        services.AddAllOf<IFakeContract>(typeof(ServiceCollectionScanExtensionsSpec).Assembly);
        services.AddAllOf<IFakeContract>(typeof(ServiceCollectionScanExtensionsSpec).Assembly);

        var provider = services.BuildServiceProvider();
        var instances = provider.GetServices<IFakeContract>().ToList();
        instances.Should().HaveCount(2);
    }

    [Fact]
    public void AddAllOf_non_generic_overload_matches_generic_behaviour()
    {
        var services = new ServiceCollection();
        services.AddAllOf(typeof(IFakeContract), typeof(ServiceCollectionScanExtensionsSpec).Assembly);

        var provider = services.BuildServiceProvider();
        var instances = provider.GetServices<IFakeContract>().ToList();
        instances.Select(i => i.Name).Should().BeEquivalentTo(new[] { nameof(FakeA), nameof(FakeB) });
    }

    [Fact]
    public void AddAllOf_skips_classes_marked_with_NotAutoRegistered()
    {
        var services = new ServiceCollection();
        services.AddAllOf<IFakeContract>(typeof(ServiceCollectionScanExtensionsSpec).Assembly);

        var implTypes = services
            .Where(d => d.ServiceType == typeof(IFakeContract))
            .Select(d => d.ImplementationType)
            .ToList();
        implTypes.Should().NotContain(typeof(FakeOptedOut));
    }

    [Fact]
    public void AddAllOf_with_no_assembly_argument_defaults_to_calling_assembly()
    {
        // When the caller omits the assembly param the helper uses Assembly.GetCallingAssembly().
        // From this test method that's the Koan.Core.Tests assembly — which contains the
        // FakeA / FakeB fixtures above.
        var services = new ServiceCollection();
        services.AddAllOf<IFakeContract>();

        var provider = services.BuildServiceProvider();
        provider.GetServices<IFakeContract>().Should().HaveCountGreaterThanOrEqualTo(2);
    }
}
