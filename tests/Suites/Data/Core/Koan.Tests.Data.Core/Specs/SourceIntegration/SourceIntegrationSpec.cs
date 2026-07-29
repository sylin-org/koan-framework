using System.Diagnostics;
using System.Text.Json;
using AwesomeAssertions;
using Koan.Core;
using Koan.Core.Hosting.App;
using Koan.Core.Semantics;
using Koan.Core.Semantics.Segmentation;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Core.SourceIntegration.Runtime;
using Koan.Data.Relational;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using KoanData = Koan.Data.Core.Data;

namespace Koan.Tests.Data.Core.Specs.SourceIntegration;

public sealed class SourceIntegrationSpec
{
    [Fact]
    public void Sql_binding_is_immutable_opaque_and_rejects_blank_text()
    {
        var binding = new SqlOperationBinding("select id from customers");

        binding.Kind.Should().Be("sql");
        binding.EffectProof.Should().Be(OperationBindingEffectProof.Opaque);
        binding.CommandText.Should().Be("select id from customers");
        FluentActions.Invoking(() => new SqlOperationBinding(" "))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Describe_and_explain_are_pure_and_share_execution_decisions_and_claims()
    {
        var integration = new FakeIntegration();
        using var host = Host(integration, source => source.Query("orders.recent", query => query
            .Native(new FakeBinding("sql", OperationBindingEffectProof.ValidatedRead))));

        var source = KoanData.Source("Legacy");
        var description = source.Describe();
        var explanation = source.Explain("orders.recent");

        integration.Activations.Should().Be(0);
        description.DecisionId.Should().Be(explanation.SourceDecisionId);
        description.Claims.Select(static claim => claim.Reference)
            .Should().BeEquivalentTo(explanation.ClaimReferences);
        description.Claims.Should().Contain(claim => claim.Profile == DataClaimProfiles.SourceCore);
        description.Claims.Should().Contain(claim => claim.Profile == DataClaimProfiles.RegisteredReads);
        explanation.Operation.Support.Should().Be(DataOperationSupport.Supported);
        explanation.Operation.Binding.Should().Be("sql");
        explanation.Operation.ParameterCount.Should().Be(0);
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var warmDescription = source.Describe();
            var warmExplanation = source.Explain("orders.recent");
            warmDescription.DecisionId.Should().Be(description.DecisionId);
            warmExplanation.SourceDecisionId.Should().Be(explanation.SourceDecisionId);
            warmExplanation.ClaimReferences.Should().Equal(explanation.ClaimReferences);
        }
        integration.Activations.Should().Be(0, "pure warm diagnostics reuse frozen decisions without provider work");
        var publicJson = JsonSerializer.Serialize(new { description, explanation });
        publicJson.Should().NotContain("primary-secret").And.NotContain("ConnectionString");

        await source.Query("orders.recent");
        integration.Activations.Should().Be(1);
    }

    [Fact]
    public async Task Doctor_is_active_non_mutating_and_timeout_is_not_caller_cancellation()
    {
        var integration = new FakeIntegration
        {
            DoctorResult = _ => Task.FromResult(new DataDoctorReceipt([
                new DataDoctorCheck(DataDoctorCodes.Connectivity, DataDoctorStatus.Healthy),
                new DataDoctorCheck(DataDoctorCodes.DeclaredShape, DataDoctorStatus.Healthy)
            ]))
        };
        using var host = Host(integration, configure: null, doctorTimeout: TimeSpan.FromMilliseconds(25));

        var healthy = await KoanData.Source("Legacy").Doctor();

        healthy.Status.Should().Be(DataDoctorStatus.Healthy);
        integration.Activations.Should().Be(1);
        integration.DoctorCalls.Should().Be(1);
        integration.RecordCalls.Should().Be(0);
        integration.ScalarCalls.Should().Be(0);

        integration.DoctorResult = async ct =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new DataDoctorReceipt([new DataDoctorCheck(DataDoctorCodes.Connectivity, DataDoctorStatus.Healthy)]);
        };
        var timedOut = await KoanData.Source("Legacy").Doctor();
        timedOut.Status.Should().Be(DataDoctorStatus.TimedOut);
        timedOut.Checks.Should().ContainSingle().Which.Code.Should().Be(DataDoctorCodes.Timeout);

        using var caller = new CancellationTokenSource();
        caller.Cancel();
        await Async(() => KoanData.Source("Legacy").Doctor(caller.Token))
            .Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Host_owns_and_disposes_only_activated_source_integrations()
    {
        var pure = new FakeIntegration();
        var pureHost = Host(pure, configure: null);
        KoanData.Source("Legacy").Describe();
        pure.Activations.Should().Be(0);
        pureHost.Dispose();
        pure.Disposals.Should().Be(0);

        var active = new FakeIntegration();
        var activeHost = Host(active, source => source.Query("read", query => query
            .Native(new FakeBinding("sql", OperationBindingEffectProof.ValidatedRead))));
        await KoanData.Source("Legacy").Query("read");
        active.Activations.Should().Be(1);
        activeHost.Dispose();
        active.Disposals.Should().Be(1);
    }

    [Fact]
    public async Task Compact_source_journey_is_entity_free_and_projects_by_ordinal()
    {
        var fields = new[] { new DataField(0, "Id"), new DataField(1, "Name") };
        var integration = new FakeIntegration
        {
            EnforceLane = true,
            Records = (_, _, _) => Task.FromResult<INeutralRecordReader>(new FakeReader(
                fields,
                [new DataRecord(fields, [7, "Ada"])]))
        };
        using var host = Host(integration, source => source.Query("customers.recent", query => query
            .Lane("Reports")
            .Sql("select id, name from customers where id >= @minimum")
            .Parameter<int>("minimum")), includeReadLane: true);

        var result = await KoanData.Source("Legacy").Query("customers.recent", new { minimum = 5 });
        var projected = result.Project<CustomerRow>();

        projected.Should().ContainSingle().Which.Should().Be(new CustomerRow(7, "Ada"));
        integration.RecordCalls.Should().Be(1);
        integration.LastPlan!.Effect.Should().Be(DataOperationEffect.Read);
        integration.LastPlan.Result.Should().Be(OperationResultKind.Records);
        integration.LastPlan.Delivery.Should().Be(OperationDelivery.Buffered);
        integration.LastParameters.Should().ContainSingle()
            .Which.Should().Be(new BoundOperationParameter("minimum", typeof(int), 5));
    }

    [Fact]
    public async Task Opaque_binding_requires_and_receives_a_provider_enforced_lane()
    {
        var denied = new FakeIntegration();
        using (var host = Host(denied, source => source.Query("opaque", query => query
                   .Native(new FakeBinding("template", OperationBindingEffectProof.Opaque)))))
        {
            Func<Task> act = () => KoanData.Source("Legacy").Query("opaque");
            await act.Should().ThrowAsync<RegisteredOperationException>()
                .WithMessage("*requires*read lane*");
            denied.RecordCalls.Should().Be(0);
            denied.Activations.Should().Be(0, "static effect and lane contradictions reject before provider activation");
        }

        var accepted = new FakeIntegration { EnforceLane = true };
        using (var host = Host(
                   accepted,
                   source => source.Query("opaque", query => query
                       .Lane("Reports")
                       .Native(new FakeBinding("template", OperationBindingEffectProof.Opaque))),
                   includeReadLane: true))
        {
            await KoanData.Source("Legacy").Query("opaque");
            accepted.LastPlan!.Lane.Should().NotBeNull();
            accepted.LastPlan.Lane!.Name.Should().Be("Reports");
            accepted.LastPlan.Lane.ConnectionIdentity.Should().NotContain("read-secret");
        }
    }

    [Fact]
    public async Task Parameter_shape_and_values_reject_before_dispatch()
    {
        var integration = new FakeIntegration();
        using var host = Host(integration, source => source.Query("typed", query => query
            .Native(new FakeBinding("sql", OperationBindingEffectProof.ValidatedRead))
            .Parameter<int>("threshold")));
        var runtime = KoanData.Source("Legacy");

        await Async(() => runtime.Query("typed")).Should().ThrowAsync<OperationParameterException>();
        await Async(() => runtime.Query("typed", new { threshold = 1, extra = 2 }))
            .Should().ThrowAsync<OperationParameterException>();
        await Async(() => runtime.Query("typed", new { threshold = "1" }))
            .Should().ThrowAsync<OperationParameterException>();
        await Async(() => runtime.Query("typed", new { threshold = (int?)null }))
            .Should().ThrowAsync<OperationParameterException>();

        integration.RecordCalls.Should().Be(0);
        integration.Activations.Should().Be(0, "parameter contradictions reject before provider activation");
        await runtime.Query("typed", new Dictionary<string, object?> { ["THRESHOLD"] = 3 });
        integration.RecordCalls.Should().Be(1);
    }

    [Fact]
    public async Task Provider_dispatch_is_never_replayed_after_failure()
    {
        var integration = new FakeIntegration
        {
            Records = (_, _, _) => throw new IOException("provider text must not cause replay")
        };
        using var host = Host(integration, source => source.Query("fragile", query => query
            .Native(new FakeBinding("sql", OperationBindingEffectProof.ValidatedRead))));

        await Async(() => KoanData.Source("Legacy").Query("fragile")).Should().ThrowAsync<IOException>();
        integration.RecordCalls.Should().Be(1);
    }

    [Fact]
    public async Task Provider_timeout_is_distinct_from_caller_cancellation_and_dispatches_once()
    {
        var integration = new FakeIntegration
        {
            Records = async (_, _, ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return new FakeReader([], []);
            }
        };
        using var host = Host(integration, source => source.Query("timeout", query => query
            .Native(new FakeBinding("sql", OperationBindingEffectProof.ValidatedRead))
            .Timeout(TimeSpan.FromMilliseconds(25))));

        await Async(() => KoanData.Source("Legacy").Query("timeout"))
            .Should().ThrowAsync<TimeoutException>();
        integration.RecordCalls.Should().Be(1);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Async(() => KoanData.Source("Legacy").Query("timeout", cancelled.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        integration.RecordCalls.Should().Be(1);
    }

    [Fact]
    public async Task Active_segmentation_fails_closed_before_dispatch()
    {
        var integration = new FakeIntegration();
        var builder = new SegmentationPlanBuilder();
        builder.ForOwner(new SemanticId("test")).Require(
            "tenant",
            () => SegmentationValue.For("tenant-a"),
            appliesTo: null,
            "Enter an explicit host scope.");
        using var host = Host(
            integration,
            source => source.Query("segmented", query => query
                .Native(new FakeBinding("sql", OperationBindingEffectProof.ValidatedRead))),
            segmentation: builder.Build());

        await Async(() => KoanData.Source("Legacy").Query("segmented"))
            .Should().ThrowAsync<RegisteredOperationException>()
            .WithMessage("*explicit host/control-plane scope*");
        integration.RecordCalls.Should().Be(0);
        integration.Activations.Should().Be(0);
    }

    [Fact]
    public async Task Scalar_enforces_exact_cardinality_conversion_and_value_bound()
    {
        var integration = new FakeIntegration
        {
            ScalarResult = new SourceScalarResult(2, 1, 42)
        };
        using var host = Host(integration, source =>
        {
            source.Scalar<int>("count", query => query
                .Native(new FakeBinding("function", OperationBindingEffectProof.ValidatedRead)));
            source.Scalar<string>("tiny", query => query
                .Native(new FakeBinding("function", OperationBindingEffectProof.ValidatedRead))
                .MaxValueBytes(2));
        });
        var runtime = KoanData.Source("Legacy");

        await Async(() => runtime.Scalar<int>("count")).Should().ThrowAsync<ScalarCardinalityException>();
        integration.ScalarResult = new SourceScalarResult(1, 1, 42);
        (await runtime.Scalar<int>("count")).Should().Be(42);
        integration.ScalarResult = new SourceScalarResult(1, 1, "abc");
        await Async(() => runtime.Scalar<string>("tiny")).Should().ThrowAsync<RegisteredOperationException>();
    }

    [Fact]
    public async Task Inspection_is_provider_neutral_source_bound_and_policy_projected()
    {
        var address = StorageAddress.From("archive", "customers");
        var reference = new FakeReference("Legacy", address);
        var descriptor = new StorageContainerDescriptor(
            reference,
            address,
            "archive/customers",
            "virtual-records",
            StorageContainerTraits.Records | StorageContainerTraits.Virtual,
            StorageContainerOperations.Describe | StorageContainerOperations.Sample |
            StorageContainerOperations.Query | StorageContainerOperations.Write);
        var inspector = new FakeInspector(descriptor)
        {
            Batch = new SourceContainerBatch(
                [descriptor],
                StorageContainerPageCompletion.MoreAvailable,
                "provider-page-2")
        };
        var integration = new FakeIntegration { Inspector = inspector };
        using var host = Host(integration, configure: null, readOnly: true, includeOtherSource: true);

        var sourceInspector = KoanData.Source("Legacy").Inspect();
        var page = await sourceInspector.Containers(10);

        page.Containers.Should().ContainSingle();
        page.Containers[0].Address.Namespace.Should().Equal("archive");
        page.Containers[0].Traits.Should().HaveFlag(StorageContainerTraits.Virtual);
        page.Containers[0].EffectiveOperations.Should().NotHaveFlag(StorageContainerOperations.Write);
        page.Continuation.Should().StartWith("koan-source-v1.");
        (await sourceInspector.Resolve(address)).Should().BeSameAs(reference);
        (await sourceInspector.Describe(reference)).ProviderKind.Should().Be("virtual-records");

        Func<Task> crossSource = () => KoanData.Source("Other").Inspect().Containers(10, page.Continuation);
        await crossSource.Should().ThrowAsync<StorageContinuationSourceMismatchException>();
        inspector.ContainerCalls.Should().Be(1);
    }

    [Fact]
    public async Task Sampling_uses_record_contract_and_rejects_non_record_container_before_sample_dispatch()
    {
        var address = StorageAddress.From("events");
        var reference = new FakeReference("Legacy", address);
        var fields = new[] { new DataField(0, "kind") };
        var descriptor = new StorageContainerDescriptor(
            reference,
            address,
            "events",
            "stream",
            StorageContainerTraits.Records,
            StorageContainerOperations.Describe | StorageContainerOperations.Sample,
            fields);
        var inspector = new FakeInspector(descriptor)
        {
            SampleReader = new FakeReader(fields, [new DataRecord(fields, ["created"])])
        };
        using var host = Host(new FakeIntegration { Inspector = inspector }, configure: null);

        var result = await KoanData.Source("Legacy").Inspect().Sample(reference, 5);
        result.Records.Should().ContainSingle();
        result.Records[0].Get<string>(0).Should().Be("created");

        inspector.Descriptor = descriptor with
        {
            Traits = StorageContainerTraits.Virtual,
            EffectiveOperations = StorageContainerOperations.Describe | StorageContainerOperations.Sample
        };
        await Async(() => KoanData.Source("Legacy").Inspect().Sample(reference, 5))
            .Should().ThrowAsync<SourceIntegrationException>();
        inspector.SampleCalls.Should().Be(1);
    }

    [Fact]
    public async Task Record_materialization_preserves_duplicates_missing_null_nested_and_projection()
    {
        var fields = new[]
        {
            new DataField(0, "duplicate", typeof(int), "int4"),
            new DataField(1, "duplicate", typeof(int), "int4"),
            new DataField(2, "Name", typeof(string)),
            new DataField(3, "Document", typeof(DataObject))
        };
        var nested = new DataObject([
            new DataProperty("tags", new DataArray(["one", 2])),
            new DataProperty("nullable", null)
        ]);
        var record = new DataRecord(fields, [1, 2, null, nested], [true, false, true, true]);
        var result = await new RecordSetMaterializer().Materialize(
            new FakeReader(fields, [record]),
            Limits(),
            "record-oracle");

        result.Completion.Should().Be(RecordSetCompletion.Complete);
        result.Records[0].FindOrdinals("duplicate").Should().Equal(0, 1);
        result.Records[0].TryGetValue(1, out _).Should().BeFalse();
        result.Records[0].TryGetValue(2, out var explicitNull).Should().BeTrue();
        explicitNull.Should().BeNull();
        result.Records[0][3].Should().BeSameAs(nested);
        result.Execution.AccountedBytes.Should().Be(
            RecordSetAccounting.MeasureShape(fields) +
            RecordSetAccounting.MeasurePresentValue(1) +
            RecordSetAccounting.MeasurePresentValue(null) +
            RecordSetAccounting.MeasurePresentValue(nested));
        var byName = () => result.Records[0]["duplicate"];
        byName.Should().Throw<RecordFieldAmbiguousException>();
    }

    [Fact]
    public void Neutral_records_and_projection_fail_correctively_for_vendor_missing_duplicate_and_type_drift()
    {
        var vendor = () => new DataRecord([new DataField(0, "Value")], [new Version(1, 2)]);
        vendor.Should().Throw<NeutralDataValueException>();

        var fields = new[] { new DataField(0, "Id"), new DataField(1, "Name") };
        var missing = new RecordSet(
            fields,
            [new DataRecord(fields, [7, null], [true, false])],
            RecordSetCompletion.Complete,
            new RecordSetExecution(Limits(), RecordSetByteAccounting.MaterializedValueV1, 0, TimeSpan.Zero));
        var missingProjection = () => missing.Project<CustomerRow>();
        missingProjection.Should().Throw<RecordValueMissingException>();

        var wrongType = new RecordSet(
            fields,
            [new DataRecord(fields, ["seven", "Ada"])],
            RecordSetCompletion.Complete,
            new RecordSetExecution(Limits(), RecordSetByteAccounting.MaterializedValueV1, 0, TimeSpan.Zero));
        var wrongProjection = () => wrongType.Project<CustomerRow>();
        wrongProjection.Should().Throw<RecordValueConversionException>();

        var duplicates = new[] { new DataField(0, "Id"), new DataField(1, "id") };
        var duplicate = new RecordSet(
            duplicates,
            [new DataRecord(duplicates, [1, 2])],
            RecordSetCompletion.Complete,
            new RecordSetExecution(Limits(), RecordSetByteAccounting.MaterializedValueV1, 0, TimeSpan.Zero));
        var duplicateProjection = () => duplicate.Project<OnlyId>();
        duplicateProjection.Should().Throw<RecordProjectionException>();
    }

    [Fact]
    public async Task Materializer_omits_first_non_fitting_record_and_reports_each_limit_truthfully()
    {
        var fields = new[] { new DataField(0, "value") };
        var records = new[]
        {
            new DataRecord(fields, ["a"]),
            new DataRecord(fields, ["a very long value"])
        };
        var materializer = new RecordSetMaterializer();

        var recordReader = new FakeReader(fields, records);
        var recordLimited = await materializer.Materialize(
            recordReader,
            Limits(maxRecords: 1),
            "record-limit");
        recordLimited.Completion.Should().Be(RecordSetCompletion.RecordLimit);
        recordLimited.Records.Should().ContainSingle();
        recordReader.ReadCalls.Should().Be(2);

        var valueLimited = await materializer.Materialize(
            new FakeReader(fields, [records[1]]),
            Limits(maxValueBytes: 5),
            "value-limit");
        valueLimited.Completion.Should().Be(RecordSetCompletion.ValueLimit);
        valueLimited.Records.Should().BeEmpty();

        var firstCost = RecordSetAccounting.MeasureShape(fields) +
                        RecordSetAccounting.MeasurePresentValue("a");
        var byteLimited = await materializer.Materialize(
            new FakeReader(fields, records),
            Limits(maxBytes: firstCost + 1),
            "byte-limit");
        byteLimited.Completion.Should().Be(RecordSetCompletion.ByteLimit);
        byteLimited.Records.Should().ContainSingle();

        var providerLimited = await materializer.Materialize(
            new FakeReader(fields, [records[0]], NeutralRecordReaderCompletion.ProviderLimit),
            Limits(),
            "provider-limit");
        providerLimited.Completion.Should().Be(RecordSetCompletion.ProviderLimit);
    }

    [Fact]
    public async Task Materializer_distinguishes_duration_cancellation_additional_channels_and_disposes()
    {
        var fields = new[] { new DataField(0, "value") };
        var materializer = new RecordSetMaterializer();
        var slow = new FakeReader(fields, [new DataRecord(fields, [1])]) { Delay = TimeSpan.FromMilliseconds(25) };

        var duration = await materializer.Materialize(
            slow,
            Limits(maxDuration: TimeSpan.FromMilliseconds(5)),
            "duration");
        duration.Completion.Should().Be(RecordSetCompletion.DurationLimit);
        duration.Records.Should().BeEmpty();
        slow.Disposed.Should().BeTrue();

        var additional = new FakeReader(fields, [], additional: true);
        await Async(() => materializer.Materialize(additional, Limits(), "additional"))
            .Should().ThrowAsync<AdditionalResultChannelsNotSupportedException>();
        additional.Disposed.Should().BeTrue();

        var cancelled = new FakeReader(fields, []);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Async(() => materializer.Materialize(cancelled, Limits(), "cancelled", cts.Token))
            .Should().ThrowAsync<OperationCanceledException>();
        cancelled.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Registered_operation_telemetry_contains_plan_truth_not_payloads()
    {
        Activity? observed = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Koan.Data.SourceIntegration",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => observed = activity
        };
        ActivitySource.AddActivityListener(listener);
        var integration = new FakeIntegration();
        using var host = Host(integration, source => source.Query("telemetry", query => query
            .Native(new FakeBinding("payload-secret", OperationBindingEffectProof.ValidatedRead))
            .Parameter<string>("secret")));

        await KoanData.Source("Legacy").Query("telemetry", new { secret = "business-secret" });

        observed.Should().NotBeNull();
        var tags = observed!.TagObjects.ToDictionary(pair => pair.Key, pair => pair.Value);
        tags["koan.data.source"].Should().Be("Legacy");
        tags["koan.data.operation"].Should().Be("telemetry");
        tags["koan.data.provider"].Should().Be("fake-source");
        tags["koan.data.attempts"].Should().Be(1);
        tags.Values.Should().NotContain(value => Equals(value, "business-secret") || Equals(value, "payload-secret"));
    }

    [Fact]
    public void Duplicate_operation_names_fail_during_composition()
    {
        var services = Services(new FakeIntegration());

        var act = () => services.AddKoan(koan =>
        {
            koan.Data.Source("Legacy").Query("duplicate", query => query
                .Native(new FakeBinding("one", OperationBindingEffectProof.ValidatedRead)));
            koan.Data.Source("Legacy").Query("duplicate", query => query
                .Native(new FakeBinding("two", OperationBindingEffectProof.ValidatedRead)));
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("*already declared*");
    }

    [Fact]
    public void Reentering_a_source_builder_is_idempotent_but_operation_names_remain_unique()
    {
        var services = Services(new FakeIntegration());

        services.AddKoan(koan =>
        {
            koan.Data.Source("Legacy").Query("first", query => query
                .Native(new FakeBinding("one", OperationBindingEffectProof.ValidatedRead)));
            koan.Data.Source("legacy").Query("second", query => query
                .Native(new FakeBinding("two", OperationBindingEffectProof.ValidatedRead)));
        });

        using var provider = services.BuildServiceProvider();
        var plans = provider.GetRequiredService<DataOperationCatalog>().Snapshot();
        plans.Select(plan => plan.Name).Should().BeEquivalentTo("first", "second");
    }

    [Fact]
    public void Runtime_source_registry_is_frozen_and_cannot_split_existing_integrations()
    {
        var integration = new FakeIntegration();
        using var host = Host(integration, configure: null);
        var registry = host.Services.GetRequiredService<DataSourceRegistry>();

        FluentActions.Invoking(() => registry.RegisterSource(new DataSourceRegistry.SourceDefinition(
                "Legacy",
                "other",
                "replacement-secret",
                new Dictionary<string, string>())))
            .Should().Throw<InvalidOperationException>().WithMessage("*after host composition*");

        var source = KoanData.Source("Legacy");
        source.Describe().Provider.Should().Be("fake-source");
        source.Describe().DecisionId.Should().Be(source.Describe().DecisionId);
        integration.Activations.Should().Be(0);
    }

    [Fact]
    public async Task Async_only_source_integration_requires_and_survives_async_host_disposal()
    {
        var integration = new AsyncOnlyIntegration();
        var source = new ResolvedSource(
            new DataSourcePlan(
                "Legacy",
                "async-only",
                StorageLifecycle.Managed,
                DataSourceAccess.ReadWrite,
                "route",
                "connection"),
            "async-only",
            DataSourceIntegrationDescriptor.Empty,
            DataClaimSet.For("async-only", static _ => { }),
            () => integration);
        _ = source.Integration;

        FluentActions.Invoking(source.Dispose)
            .Should().Throw<InvalidOperationException>().WithMessage("*DisposeAsync*");
        await source.DisposeAsync();

        integration.Disposals.Should().Be(1);
    }

    [Fact]
    public async Task Source_service_retries_failed_activation_and_async_disposal_remains_recoverable()
    {
        var integration = new AsyncOnlyIntegration();
        var factory = new AsyncOnlyFactory(integration) { FailNextCreate = true };
        var registry = new DataSourceRegistry();
        registry.RegisterSource(new DataSourceRegistry.SourceDefinition(
            "Legacy",
            factory.Provider,
            "source-secret",
            new Dictionary<string, string>()));
        registry.Freeze();
        await using var services = new ServiceCollection().BuildServiceProvider();
        var service = new DataSourceIntegrationService(services, registry, [factory]);
        var source = service.Resolve("Legacy");

        FluentActions.Invoking(() => source.Integration)
            .Should().Throw<InvalidOperationException>().WithMessage("*injected activation failure*");
        source.Integration.Should().BeSameAs(integration);
        factory.CreateCalls.Should().Be(2);

        FluentActions.Invoking(service.Dispose)
            .Should().Throw<InvalidOperationException>().WithMessage("*DisposeAsync*");
        await service.DisposeAsync();

        integration.Disposals.Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_source_activation_publishes_one_provider_resource()
    {
        var integration = new AsyncOnlyIntegration();
        var factory = new AsyncOnlyFactory(integration) { Delay = TimeSpan.FromMilliseconds(40) };
        var registry = new DataSourceRegistry();
        registry.RegisterSource(new DataSourceRegistry.SourceDefinition(
            "Legacy",
            factory.Provider,
            "source-secret",
            new Dictionary<string, string>()));
        registry.Freeze();
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var service = new DataSourceIntegrationService(services, registry, [factory]);
        var source = service.Resolve("Legacy");

        var integrations = await Task.WhenAll(Enumerable.Range(0, 24)
            .Select(_ => Task.Run(() => source.Integration)));

        integrations.Should().OnlyContain(candidate => ReferenceEquals(candidate, integration));
        factory.CreateCalls.Should().Be(1);
    }

    private static HostHarness Host(
        FakeIntegration integration,
        Action<DataSourceBuilder>? configure,
        bool readOnly = false,
        bool includeReadLane = false,
        bool includeOtherSource = false,
        SegmentationPlan? segmentation = null,
        TimeSpan? doctorTimeout = null)
    {
        var services = Services(integration, readOnly, includeReadLane, includeOtherSource, doctorTimeout);
        services.AddKoan(koan =>
        {
            if (configure is not null) configure(koan.Data.Source("Legacy"));
        });
        if (segmentation is not null) services.AddSingleton(segmentation);
        var provider = services.BuildServiceProvider();
        return new HostHarness(provider, AppHost.PushScope(provider));
    }

    private static ServiceCollection Services(
        FakeIntegration integration,
        bool readOnly = false,
        bool includeReadLane = false,
        bool includeOtherSource = false,
        TimeSpan? doctorTimeout = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Koan:Data:Sources:Legacy:Adapter"] = "fake-source",
            ["Koan:Data:Sources:Legacy:ConnectionString"] = "primary-secret",
            ["Koan:Data:Sources:Legacy:Access"] = readOnly ? "ReadOnly" : "ReadWrite"
        };
        if (includeReadLane)
            values["Koan:Data:Sources:Legacy:ReadLanes:Reports:ConnectionString"] = "read-secret";
        if (doctorTimeout is not null)
            values["Koan:Data:SourceIntegration:DoctorTimeout"] = doctorTimeout.Value.ToString("c");
        if (includeOtherSource)
        {
            values["Koan:Data:Sources:Other:Adapter"] = "fake-source";
            values["Koan:Data:Sources:Other:ConnectionString"] = "other-secret";
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IDataSourceIntegrationFactory>(new FakeFactory(integration));
        return services;
    }

    private static RecordSetLimits Limits(
        int maxRecords = 10,
        long maxBytes = 1_000,
        long maxValueBytes = 500,
        TimeSpan? maxDuration = null) => new(
            maxRecords,
            maxBytes,
            maxValueBytes,
            maxDuration ?? TimeSpan.FromSeconds(1));

    private static Func<Task> Async(Func<Task> action) => action;

    private sealed record CustomerRow(int Id, string Name);
    private sealed record OnlyId(int Id);
    private sealed record FakeBinding(string Kind, OperationBindingEffectProof EffectProof) : IDataOperationBinding;

    private sealed class FakeFactory(FakeIntegration integration) : IDataSourceIntegrationFactory
    {
        public string Provider => "fake-source";
        public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new();
        public void DescribeClaims(IDataClaims claims) => claims.Profile(DataClaimProfiles.RegisteredReads);
        public DataSourceIntegrationDescriptor DescribeSource(string source) => new(
            SourceIntegrationCapabilities.RegisteredRecords | SourceIntegrationCapabilities.RegisteredScalar,
            SourceInspectionCapabilities.None,
            ["sql", "template"],
            enforcesReadLanes: integration.EnforceLane,
            supportsDoctor: true);
        public IDataSourceIntegration CreateSource(IServiceProvider services, string source)
        {
            integration.Activations++;
            return integration;
        }
    }

    private sealed class FakeIntegration : IDataSourceIntegration, IDataSourceDoctor, IDisposable
    {
        public SourceIntegrationCapabilities Capabilities { get; set; } =
            SourceIntegrationCapabilities.RegisteredRecords | SourceIntegrationCapabilities.RegisteredScalar;
        public IDataSourceInspectorAdapter? Inspector { get; set; }
        public bool EnforceLane { get; set; }
        public int RecordCalls { get; private set; }
        public int ScalarCalls { get; private set; }
        public int DoctorCalls { get; private set; }
        public int Activations { get; set; }
        public int Disposals { get; private set; }
        public OperationPlan? LastPlan { get; private set; }
        public IReadOnlyList<BoundOperationParameter>? LastParameters { get; private set; }
        public SourceScalarResult ScalarResult { get; set; } = new(1, 1, 1);
        public Func<OperationPlan, IReadOnlyList<BoundOperationParameter>, CancellationToken, Task<INeutralRecordReader>>?
            Records { get; set; }
        public Func<CancellationToken, Task<DataDoctorReceipt>> DoctorResult { get; set; } = _ =>
            Task.FromResult(new DataDoctorReceipt([
                new DataDoctorCheck(DataDoctorCodes.Connectivity, DataDoctorStatus.Healthy)
            ]));

        public bool Supports(IDataOperationBinding binding, OperationResultKind result) => true;
        public bool EnforcesReadLane(DataReadLanePlan lane) => EnforceLane;

        public Task<INeutralRecordReader> ExecuteRecords(
            OperationPlan plan,
            IReadOnlyList<BoundOperationParameter> parameters,
            CancellationToken ct = default)
        {
            RecordCalls++;
            LastPlan = plan;
            LastParameters = parameters;
            return Records?.Invoke(plan, parameters, ct) ?? Task.FromResult<INeutralRecordReader>(
                new FakeReader([], []));
        }

        public Task<SourceScalarResult> ExecuteScalar(
            OperationPlan plan,
            IReadOnlyList<BoundOperationParameter> parameters,
            CancellationToken ct = default)
        {
            ScalarCalls++;
            LastPlan = plan;
            LastParameters = parameters;
            return Task.FromResult(ScalarResult);
        }

        public Task<DataDoctorReceipt> Doctor(CancellationToken ct = default)
        {
            DoctorCalls++;
            return DoctorResult(ct);
        }

        public void Dispose() => Disposals++;
    }

    private sealed class AsyncOnlyIntegration : IDataSourceIntegration, IAsyncDisposable
    {
        public int Disposals { get; private set; }
        public SourceIntegrationCapabilities Capabilities => SourceIntegrationCapabilities.None;
        public IDataSourceInspectorAdapter? Inspector => null;
        public bool Supports(IDataOperationBinding binding, OperationResultKind result) => false;
        public bool EnforcesReadLane(DataReadLanePlan lane) => false;
        public Task<INeutralRecordReader> ExecuteRecords(
            OperationPlan plan,
            IReadOnlyList<BoundOperationParameter> parameters,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SourceScalarResult> ExecuteScalar(
            OperationPlan plan,
            IReadOnlyList<BoundOperationParameter> parameters,
            CancellationToken ct = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AsyncOnlyFactory(AsyncOnlyIntegration integration) : IDataSourceIntegrationFactory
    {
        public string Provider => "async-only";
        public int CreateCalls { get; private set; }
        public bool FailNextCreate { get; set; }
        public TimeSpan Delay { get; init; }
        public StorageNamingCapability GetNamingCapability(IServiceProvider services) => new();
        public void DescribeClaims(IDataClaims claims) { }

        public IDataSourceIntegration CreateSource(IServiceProvider services, string source)
        {
            CreateCalls++;
            if (Delay > TimeSpan.Zero) Thread.Sleep(Delay);
            if (FailNextCreate)
            {
                FailNextCreate = false;
                throw new InvalidOperationException("injected activation failure");
            }

            return integration;
        }
    }

    private sealed class FakeReader : INeutralRecordReader
    {
        private readonly Queue<DataRecord> _records;

        public FakeReader(
            IReadOnlyList<DataField> fields,
            IEnumerable<DataRecord> records,
            NeutralRecordReaderCompletion completion = NeutralRecordReaderCompletion.Complete,
            bool additional = false)
        {
            Fields = fields;
            _records = new Queue<DataRecord>(records);
            Completion = completion;
            HasAdditionalResultChannels = additional;
        }

        public IReadOnlyList<DataField> Fields { get; }
        public NeutralRecordReaderCompletion Completion { get; }
        public bool HasAdditionalResultChannels { get; }
        public TimeSpan Delay { get; set; }
        public int ReadCalls { get; private set; }
        public bool Disposed { get; private set; }

        public async ValueTask<DataRecord?> Read(CancellationToken ct = default)
        {
            ReadCalls++;
            if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
            return _records.TryDequeue(out var record) ? record : null;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeReference(string source, StorageAddress address)
        : StorageContainerReference(source, address);

    private sealed class FakeInspector(StorageContainerDescriptor descriptor) : IDataSourceInspectorAdapter
    {
        public SourceInspectionCapabilities Capabilities { get; set; } =
            SourceInspectionCapabilities.ListContainers |
            SourceInspectionCapabilities.ResolveAddress |
            SourceInspectionCapabilities.DescribeContainer |
            SourceInspectionCapabilities.SampleRecords;
        public StorageContainerDescriptor Descriptor { get; set; } = descriptor;
        public SourceContainerBatch? Batch { get; set; }
        public INeutralRecordReader? SampleReader { get; set; }
        public int ContainerCalls { get; private set; }
        public int SampleCalls { get; private set; }

        public Task<SourceContainerBatch> Containers(
            int take,
            string? providerContinuation,
            CancellationToken ct = default)
        {
            ContainerCalls++;
            return Task.FromResult(Batch ?? new SourceContainerBatch(
                [Descriptor], StorageContainerPageCompletion.Complete, null));
        }

        public Task<StorageContainerReference> Resolve(StorageAddress address, CancellationToken ct = default) =>
            Task.FromResult(Descriptor.Reference);

        public Task<StorageContainerDescriptor> Describe(
            StorageContainerReference reference,
            CancellationToken ct = default) => Task.FromResult(Descriptor);

        public Task<INeutralRecordReader> Sample(
            StorageContainerReference reference,
            int take,
            CancellationToken ct = default)
        {
            SampleCalls++;
            return Task.FromResult(SampleReader ?? new FakeReader([], []));
        }
    }

    private sealed class HostHarness(ServiceProvider services, IDisposable scope) : IDisposable
    {
        public IServiceProvider Services => services;

        public void Dispose()
        {
            scope.Dispose();
            services.Dispose();
        }
    }
}
