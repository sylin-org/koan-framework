using System.IO;
using AwesomeAssertions;
using Koan.Core.Composition;
using Koan.Core.Diagnostics;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Hosting.Registry;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Composition;
using Koan.Core.Semantics;
using Koan.Core.Semantics.Contributions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Koan.Core.Tests.Semantics;

public sealed class SemanticContributionLifecycleSpec
{
    private const string GeneratedId = "Sylin.Koan.Tests.Semantics.GeneratedContribution";
    private const string FirstId = "Sylin.Koan.Tests.Semantics.FirstContribution";
    private const string SecondId = "Sylin.Koan.Tests.Semantics.SecondContribution";
    private const string DiscoveryId = "Sylin.Koan.Tests.Semantics.DiscoveryContribution";

    [Fact]
    public void Generated_binding_applies_the_exact_retained_module_after_the_application_callback()
    {
        var descriptor = GeneratedDescriptor();
        var (services, session, runtime) = Prepare(Manifest("package", GeneratedId), descriptor);
        var timeline = new List<string>();
        var target = new GeneratedContributionTarget(timeline);

        session.ScheduleContributions(
            _ => target,
            () =>
            {
                timeline.Add("freeze");
                return new GeneratedPlan(target.Modules.ToArray());
            },
            static (collection, plan) => collection.AddSingleton(plan));

        services.AddKoan(() =>
        {
            timeline.Add("application");
            target.Modules.Should().BeEmpty();
        });

        using var provider = services.BuildServiceProvider();
        var retained = runtime.GetModule(new SemanticId(GeneratedId));
        provider.GetRequiredService<GeneratedPlan>().Modules.Should().ContainSingle().Which.Should().BeSameAs(retained);
        provider.GetRequiredService<SemanticContributionCompilationSnapshot>()
            .Get<GeneratedContributionTarget>().AppliedOwners
            .Should().Equal(new SemanticId(GeneratedId));
        timeline.Should().Equal("application", "contribute", "freeze");
    }

    [Theory]
    [InlineData("project")]
    [InlineData("package")]
    public void Project_and_package_manifests_dispatch_equivalent_generated_and_reflection_bindings(string kind)
    {
        var generated = GeneratedDescriptor();
        var reflected = new SemanticComponentDescriptor(
            GeneratedId,
            typeof(GeneratedContributionModule),
            static () => new GeneratedContributionModule(),
            contributionBindings: RegistryManifestLoader.BuildSemanticContributionBindings(
                typeof(GeneratedContributionModule)));
        var descriptor = kind == "project" ? generated : reflected;
        var constitution = SemanticActivationCompiler.Compile(Manifest(kind, GeneratedId), [descriptor]);
        var runtime = SemanticModuleRuntime.Create(constitution);
        var target = new GeneratedContributionTarget([]);

        var snapshot = SemanticContributionCompiler.Compile(
            constitution,
            runtime,
            _ => target);

        target.Modules.Should().ContainSingle().Which.Should().BeSameAs(runtime.GetModule(new SemanticId(GeneratedId)));
        snapshot.AppliedOwners.Should().Equal(new SemanticId(GeneratedId));
    }

    [Fact]
    public void Inactive_contributor_never_runs_its_factory_or_binding()
    {
        var factoryCalls = 0;
        var bindingCalls = 0;
        var descriptor = new SemanticComponentDescriptor(
            FirstId,
            typeof(FirstProbeModule),
            () =>
            {
                factoryCalls++;
                return new FirstProbeModule();
            },
            contributionBindings:
            [
                Binding<FirstProbeModule, ProbeTarget>((_, _) => bindingCalls++),
            ]);
        var (services, session, _) = Prepare(
            Manifest("package", "Sylin.Koan.Tests.Semantics.Unrelated"),
            descriptor);
        var values = new List<string>();

        session.ScheduleContributions(
            owner => new ProbeTarget(owner, values),
            () => new ProbePlan(values.ToArray()),
            static (collection, plan) => collection.AddSingleton(plan));
        services.AddKoan();

        factoryCalls.Should().Be(0);
        bindingCalls.Should().Be(0);
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ProbePlan>().Values.Should().BeEmpty();
        provider.GetRequiredService<SemanticContributionCompilationSnapshot>()
            .Get<ProbeTarget>().Decisions.Should().ContainSingle()
            .Which.State.Should().Be(SemanticDecisionState.Inactive);
    }

    [Fact]
    public void Contributions_follow_constitution_order_not_descriptor_input_order()
    {
        var first = Descriptor(
            FirstId,
            static () => new FirstProbeModule(),
            Binding<FirstProbeModule, ProbeTarget>(static (module, target) => module.Apply(target)));
        var second = Descriptor(
            SecondId,
            static () => new SecondProbeModule(),
            Binding<SecondProbeModule, ProbeTarget>(static (module, target) => module.Apply(target)));
        var (services, session, _) = Prepare(
            Manifest("package", FirstId, SecondId),
            second,
            first);
        var values = new List<string>();

        session.ScheduleContributions(
            owner => new ProbeTarget(owner, values),
            () => new ProbePlan(values.ToArray()),
            static (collection, plan) => collection.AddSingleton(plan));
        services.AddKoan();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ProbePlan>().Values.Should().Equal(FirstId, SecondId);
        provider.GetRequiredService<SemanticContributionCompilationSnapshot>()
            .Get<ProbeTarget>().AppliedOwners.Should().Equal(new SemanticId(FirstId), new SemanticId(SecondId));
    }

    [Fact]
    public void Nested_and_repeated_AddKoan_finalize_one_time_after_the_outer_callback()
    {
        var applyCalls = 0;
        var descriptor = Descriptor(
            FirstId,
            static () => new FirstProbeModule(),
            Binding<FirstProbeModule, ProbeTarget>((_, target) =>
            {
                applyCalls++;
                target.Record();
            }));
        var (services, session, _) = Prepare(Manifest("package", FirstId), descriptor);
        var values = new List<string>();
        session.ScheduleContributions(
            owner => new ProbeTarget(owner, values),
            () => new ProbePlan(values.ToArray()),
            static (collection, plan) => collection.AddSingleton(plan));

        services.AddKoan(() =>
        {
            services.AddKoan(() =>
                services.Should().NotContain(service => service.ServiceType == typeof(ProbePlan)));
            services.Should().NotContain(service => service.ServiceType == typeof(ProbePlan));
        });
        services.AddKoan();
        services.AddKoan();

        applyCalls.Should().Be(1);
        services.Where(service => service.ServiceType == typeof(ProbePlan)).Should().ContainSingle();
        services.Where(service => service.ServiceType == typeof(SemanticContributionCompilationSnapshot))
            .Should().ContainSingle();
    }

    [Fact]
    public void Two_hosts_retain_distinct_modules_plans_and_snapshots()
    {
        var descriptor = Descriptor(
            FirstId,
            static () => new FirstProbeModule(),
            Binding<FirstProbeModule, HostTarget>(static (module, target) => target.Module = module));

        var first = ComposeHost(descriptor);
        var second = ComposeHost(descriptor);

        first.Module.Should().NotBeSameAs(second.Module);
        first.Plan.Should().NotBeSameAs(second.Plan);
        first.Snapshot.Should().NotBeSameAs(second.Snapshot);
        first.Plan.Module.Should().BeSameAs(first.Module);
        second.Plan.Module.Should().BeSameAs(second.Module);
    }

    [Fact]
    public void A_later_target_failure_commits_no_target_plan_or_snapshot()
    {
        var descriptor = new SemanticComponentDescriptor(
            FirstId,
            typeof(AtomicProbeModule),
            static () => new AtomicProbeModule(),
            contributionBindings:
            [
                Binding<AtomicProbeModule, AtomicFirstTarget>(static (module, target) => module.Apply(target)),
                Binding<AtomicProbeModule, AtomicSecondTarget>(static (module, target) => module.Apply(target)),
            ]);
        var (services, session, _) = Prepare(Manifest("package", FirstId), descriptor);
        var committed = 0;
        session.ScheduleContributions(
            _ => new AtomicFirstTarget(),
            static () => new AtomicFirstPlan(),
            (collection, plan) =>
            {
                committed++;
                collection.AddSingleton(plan);
            });
        session.ScheduleContributions<AtomicSecondTarget, AtomicSecondPlan>(
            _ => new AtomicSecondTarget(),
            static () => throw new InvalidOperationException("planted target rejection"),
            (collection, plan) =>
            {
                committed++;
                collection.AddSingleton(plan);
            });

        var compose = () => services.AddKoan();

        compose.Should().Throw<InvalidOperationException>().WithMessage("*planted target rejection*");
        committed.Should().Be(0);
        services.Should().NotContain(service => service.ServiceType == typeof(AtomicFirstPlan));
        services.Should().NotContain(service => service.ServiceType == typeof(AtomicSecondPlan));
        services.Should().NotContain(service => service.ServiceType == typeof(SemanticContributionCompilationSnapshot));
        var retry = () => services.AddKoan();
        retry.Should().Throw<InvalidOperationException>().WithMessage("*composition failed*new service collection*");
    }

    [Fact]
    public void The_discovery_concern_compiles_active_module_sources_without_application_glue()
    {
        var descriptor = new SemanticComponentDescriptor(
            DiscoveryId,
            typeof(GeneratedDiscoveryModule),
            static () => new GeneratedDiscoveryModule(),
            contributionBindings: RegistryManifestLoader.BuildSemanticContributionBindings(
                typeof(GeneratedDiscoveryModule)));
        var (services, _, runtime) = Prepare(Manifest("package", DiscoveryId), descriptor);
        services.AddKoan();

        using var provider = services.BuildServiceProvider();
        var plan = provider.GetRequiredService<ServiceDiscoveryPlan>();
        var source = plan.Sources.Should().ContainSingle().Subject;
        source.Owner.Should().Be(DiscoveryId);
        source.Id.Should().Be("source.generated");
        source.IntentSchemes.Should().Equal("generated");
        provider.GetRequiredService<GeneratedDiscoverySource>().Should().NotBeNull();
        runtime.GetModule(new SemanticId(DiscoveryId)).Should().BeOfType<GeneratedDiscoveryModule>();
        var fact = provider.GetRequiredService<SemanticContributionCompilationSnapshot>()
            .ToFacts()
            .Should().ContainSingle().Subject;
        fact.Code.Should().Be("koan.semantic.contribution.applied");
        fact.State.Should().Be(KoanFactState.Selected);
        fact.Subject.Should().Contain("DiscoveryContributionTarget").And.Contain(DiscoveryId);
    }

    private static HostResult ComposeHost(SemanticComponentDescriptor descriptor)
    {
        var (services, session, runtime) = Prepare(Manifest("package", FirstId), descriptor);
        var target = new HostTarget();
        session.ScheduleContributions(
            _ => target,
            () => new HostPlan(target.Module!),
            static (collection, plan) => collection.AddSingleton(plan));
        services.AddKoan();
        var provider = services.BuildServiceProvider();
        return new HostResult(
            provider,
            runtime.GetModule(new SemanticId(FirstId)),
            provider.GetRequiredService<HostPlan>(),
            provider.GetRequiredService<SemanticContributionCompilationSnapshot>());
    }

    private static (
        ServiceCollection Services,
        SemanticCompositionSession Session,
        SemanticModuleRuntime Runtime) Prepare(
        KoanApplicationReferenceManifest manifest,
        params SemanticComponentDescriptor[] descriptors)
    {
        var services = new ServiceCollection();
        var session = SemanticCompositionSession.GetOrCreate(services);
        var constitution = SemanticActivationCompiler.Compile(manifest, descriptors);
        var runtime = SemanticModuleRuntime.Create(constitution);
        session.CompleteModuleInitialization(manifest, constitution, runtime);
        runtime.Register(services);
        return (services, session, runtime);
    }

    private static SemanticComponentDescriptor GeneratedDescriptor() =>
        new(
            GeneratedId,
            typeof(GeneratedContributionModule),
            static () => new GeneratedContributionModule(),
            contributionBindings: RegistryManifestLoader.BuildSemanticContributionBindings(
                typeof(GeneratedContributionModule)));

    private static SemanticComponentDescriptor Descriptor<TModule>(
        string id,
        Func<TModule> factory,
        SemanticContributionBinding binding)
        where TModule : KoanModule =>
        new(
            id,
            typeof(TModule),
            () => factory(),
            contributionBindings: [binding]);

    private static SemanticContributionBinding Binding<TModule, TTarget>(Action<TModule, TTarget> apply)
        where TModule : KoanModule =>
        new(
            typeof(TTarget),
            (module, target) => apply((TModule)module, (TTarget)target));

    private static KoanApplicationReferenceManifest Manifest(string kind, params string[] ids)
    {
        var lines = ids.Select(id => kind == "project"
            ? $"reference|project|Koan.Tests.Semantics|{id}"
            : $"reference|package|{id}|{id}");
        using var reader = new StringReader(
            string.Join(Environment.NewLine, new[] { "schema|1" }.Concat(lines)));
        return KoanApplicationReferenceManifest.Parse(reader);
    }

    private sealed class FirstProbeModule : KoanModule
    {
        public void Apply(ProbeTarget target) => target.Record();
    }

    private sealed class SecondProbeModule : KoanModule
    {
        public void Apply(ProbeTarget target) => target.Record();
    }

    private sealed class ProbeTarget(SemanticId owner, List<string> values)
    {
        public void Record() => values.Add(owner.Value);
    }

    private sealed record ProbePlan(IReadOnlyList<string> Values);

    private sealed class HostTarget
    {
        public KoanModule? Module { get; set; }
    }

    private sealed record HostPlan(KoanModule Module);

    private sealed record HostResult(
        ServiceProvider Provider,
        KoanModule Module,
        HostPlan Plan,
        SemanticContributionCompilationSnapshot Snapshot);

    private sealed class AtomicProbeModule : KoanModule
    {
        public void Apply(AtomicFirstTarget target) => target.Applied = true;

        public void Apply(AtomicSecondTarget target) => target.Applied = true;
    }

    private sealed class AtomicFirstTarget
    {
        public bool Applied { get; set; }
    }

    private sealed class AtomicSecondTarget
    {
        public bool Applied { get; set; }
    }

    private sealed record AtomicFirstPlan;

    private sealed record AtomicSecondPlan;
}

internal sealed class GeneratedContributionModule : KoanModule, IContributeTo<GeneratedContributionTarget>
{
    public void Contribute(GeneratedContributionTarget target) => target.Record(this);
}

internal sealed class GeneratedContributionTarget(List<string> timeline)
{
    public List<KoanModule> Modules { get; } = [];

    public void Record(KoanModule module)
    {
        Modules.Add(module);
        timeline.Add("contribute");
    }
}

internal sealed record GeneratedPlan(IReadOnlyList<KoanModule> Modules);

internal sealed class GeneratedDiscoveryModule : KoanModule, IContributeTo<DiscoveryContributionTarget>
{
    public void Contribute(DiscoveryContributionTarget target) =>
        target.AddSource<GeneratedDiscoverySource>("source.generated", "generated");
}

internal sealed class GeneratedDiscoverySource : IDiscoveryCandidateSource
{
    public Task<IReadOnlyList<DiscoveryCandidate>> GetCandidates(
        DiscoveryCandidateRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DiscoveryCandidate>>([]);
}
