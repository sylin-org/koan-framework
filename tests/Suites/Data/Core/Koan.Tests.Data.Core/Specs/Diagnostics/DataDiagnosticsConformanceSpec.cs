using AwesomeAssertions;
using Koan.Core.Composition;
using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core.Composition;
using Koan.Data.Abstractions.Failures;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Pipeline;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Koan.Tests.Data.Core.Specs.Diagnostics;

public sealed class DataDiagnosticsConformanceSpec
{
    private sealed class Alpha { }
    private sealed class Beta { }

    [Fact]
    public void Mutable_composition_and_name_caches_are_isolated_between_two_hosts()
    {
        var firstServices = new ServiceCollection();
        using (KoanCompositionScope.Enter(firstServices))
            ManagedFieldRegistry.Register(new ManagedFieldDescriptor("__alpha", typeof(string), () => "a", _ => true));
        firstServices.AddSingleton(new StorageNameCache(8));
        using var first = firstServices.BuildServiceProvider();

        var secondServices = new ServiceCollection();
        using (KoanCompositionScope.Enter(secondServices))
            ManagedFieldRegistry.Register(new ManagedFieldDescriptor("__beta", typeof(string), () => "b", _ => true));
        secondServices.AddSingleton(new StorageNameCache(8));
        using var second = secondServices.BuildServiceProvider();

        using (AppHost.PushScope(first))
        {
            ManagedFieldRegistry.All.Select(static item => item.StorageName).Should().Equal("__alpha");
            StorageNameGenerator.Resolve("same", typeof(Alpha), null,
                () => new StorageNamingCapability { NameOverride = _ => "first" }).Should().Be("first");
        }
        using (AppHost.PushScope(second))
        {
            ManagedFieldRegistry.All.Select(static item => item.StorageName).Should().Equal("__beta");
            StorageNameGenerator.Resolve("same", typeof(Alpha), null,
                () => new StorageNamingCapability { NameOverride = _ => "second" }).Should().Be("second");
        }
    }

    [Fact]
    public void Diagnostic_categories_are_bounded_without_failing_business_observation()
    {
        var options = Options.Create(new DataRuntimeOptions { DiagnosticEntries = 2 });
        var diagnostics = new DataDiagnostics([], options);
        for (var index = 0; index < 5; index++)
        {
            diagnostics.Observe(new EntityConfigInfo($"Entity{index}", "String", "fake", "Id"));
            diagnostics.ObserveParticipation("fake", $"Source{index}");
            diagnostics.ObserveSourcePlan(new DataSourcePlan(
                $"Source{index}", "fake", StorageLifecycle.External, DataSourceAccess.ReadOnly,
                $"route-{index}", $"connection-{index}"));
        }

        diagnostics.GetEntityConfigsSnapshot().Should().HaveCount(2);
        diagnostics.GetAdapterParticipationsSnapshot().Should().HaveCount(2);
        diagnostics.GetSourcePlansSnapshot().Should().HaveCount(2);
        diagnostics.GetSourcePlansSnapshot().Should().OnlyContain(static plan =>
            !plan.ToString().Contains("connection-", StringComparison.Ordinal));
    }

    [Fact]
    public void Native_evidence_is_bounded_exact_and_contains_no_message_or_exception_object()
    {
        var options = Options.Create(new DataRuntimeOptions { NativeEvidenceEntries = 2 });
        var store = new DataNativeEvidenceStore(options);
        var context = new DataNativeEvidenceContext("fake", DataNativeTargetKind.Source, "doctor.execute");

        var expired = store.Record(new InvalidOperationException("secret-one"), context, "CODE-1");
        store.Record(new IOException("secret-two"), context, "CODE-2");
        var retained = store.Record(new TimeoutException("secret-three"), context, "CODE-3");

        store.TryGet(expired, out _).Should().BeFalse();
        store.TryGet(retained, out var evidence).Should().BeTrue();
        evidence.NativeType.Should().Be(typeof(TimeoutException).FullName);
        evidence.NativeCode.Should().Be("CODE-3");
        evidence.ToString().Should().NotContain("secret-three");
        typeof(DataNativeEvidenceRecord).GetProperties()
            .Should().NotContain(property => typeof(Exception).IsAssignableFrom(property.PropertyType) ||
                                             property.Name.Contains("Message", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Runtime_facts_project_exact_route_and_claim_references_without_physical_identity()
    {
        var diagnostics = new DataDiagnostics([], Options.Create(new DataRuntimeOptions()));
        var claims = DataClaimSet.For("sample", declaration => declaration
            .Profile(DataClaimProfiles.RegisteredReads, advertised: true));
        diagnostics.ObserveSourcePlan(
            new DataSourcePlan(
                "Legacy",
                "sample",
                StorageLifecycle.External,
                DataSourceAccess.ReadOnly,
                "decision-alpha",
                "credential-identity"),
            claims);
        var registrations = new ServiceCollection();
        registrations.AddSingleton<IDataDiagnostics>(diagnostics);
        using var services = registrations.BuildServiceProvider();
        var builder = new KoanCompositionBuilder();

        DataCompositionFacts.Project(builder, services, "test");
        builder.ApplyTo(out _, out var capabilities, out _, out _, out var facts);

        capabilities!["data:source:decision-alpha"].Should().Equal(
            claims.Claims.Select(static claim => claim.Reference));
        facts.Should().ContainSingle(fact =>
            fact.Code == Koan.Data.Core.Infrastructure.Constants.Diagnostics.Codes.SourcePlanSelected &&
            fact.Subject == "data:source:decision-alpha");
        string.Join('|', facts.Select(static fact => fact.ToString()))
            .Should().NotContain("Legacy").And.NotContain("credential-identity");
    }

    [Fact]
    public void Pure_source_descriptor_projects_complete_nonduplicated_runtime_claims_for_the_selected_source()
    {
        var factory = new DescribedSourceFactory();

        var claims = DataClaimSet.Describe(factory, "Legacy");

        factory.DescribedSource.Should().Be("Legacy");
        factory.Activations.Should().Be(0);
        claims.Claims.Select(static claim => claim.Profile).Should().BeEquivalentTo(
            DataClaimProfiles.SourceCore,
            DataClaimProfiles.RegisteredReads,
            DataClaimProfiles.RecordResults,
            DataClaimProfiles.ContainerListing,
            DataClaimProfiles.ContainerAddressResolution,
            DataClaimProfiles.ContainerDescription,
            DataClaimProfiles.RecordSampling);
        claims.Claims.Should().ContainSingle(claim => claim.Profile == DataClaimProfiles.RegisteredReads);
        claims.Claims.Where(static claim => claim.Profile != DataClaimProfiles.SourceCore)
            .Should().OnlyContain(static claim => claim.Advertised);
    }

    [Fact]
    public void Scalar_only_source_descriptor_does_not_overclaim_record_results()
    {
        var factory = new DescribedSourceFactory(new DataSourceIntegrationDescriptor(
            SourceIntegrationCapabilities.RegisteredScalar));

        var claims = DataClaimSet.Describe(factory, "Legacy");

        claims.Claims.Should().Contain(claim => claim.Profile == DataClaimProfiles.RegisteredReads);
        claims.Claims.Should().NotContain(claim => claim.Profile == DataClaimProfiles.RecordResults);
    }

    private sealed class DescribedSourceFactory : IDataSourceIntegrationFactory
    {
        private readonly DataSourceIntegrationDescriptor _descriptor;

        public DescribedSourceFactory(DataSourceIntegrationDescriptor? descriptor = null)
        {
            _descriptor = descriptor ?? new DataSourceIntegrationDescriptor(
                SourceIntegrationCapabilities.RegisteredRecords | SourceIntegrationCapabilities.RegisteredScalar,
                SourceInspectionCapabilities.ListContainers |
                SourceInspectionCapabilities.ResolveAddress |
                SourceInspectionCapabilities.DescribeContainer |
                SourceInspectionCapabilities.SampleRecords,
                ["template"]);
        }

        public string Provider => "described-source";
        public string? DescribedSource { get; private set; }
        public int Activations { get; private set; }

        public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new();

        public void DescribeClaims(IDataClaims claims) =>
            claims.Profile(DataClaimProfiles.RegisteredReads);

        public DataSourceIntegrationDescriptor DescribeSource(string source)
        {
            DescribedSource = source;
            return _descriptor;
        }

        public IDataSourceIntegration CreateSource(IServiceProvider services, string source)
        {
            Activations++;
            throw new InvalidOperationException("Pure claim description must not activate Source Integration.");
        }
    }
}
