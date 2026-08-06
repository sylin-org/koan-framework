using System.Diagnostics;
using Koan.Core.Diagnostics;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Data.Core.Composition;
using Koan.Data.Core.Routing;
using Koan.Data.Cutover.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Koan.Data.Cutover.Runtime;

internal sealed class DefaultRouteTransitionService(
    IServiceProvider services,
    DataSourceRegistry sources,
    DataProviderCatalog providers,
    DefaultDataRouteAuthority authority,
    DataOperationHorizon horizon,
    DataApplicationManifest manifest,
    IDataMappingPlans mappings,
    IOptions<DataCutoverOptions> options)
{
    private readonly DataCutoverOptions _options = Validate(options?.Value
        ?? throw new ArgumentNullException(nameof(options)));

    internal async Task<DefaultRouteTransitionPlan> Plan(string targetSource, CancellationToken ct)
    {
        var operationId = Guid.CreateVersion7().ToString("n");
        var plan = (await Prepare(operationId, targetSource, ct).ConfigureAwait(false)).Plan;
        RecordPlan(plan);
        return plan;
    }

    internal async Task<DefaultRouteTransitionReceipt> Run(string targetSource, CancellationToken ct)
    {
        var operationId = Guid.CreateVersion7().ToString("n");
        var initial = await Prepare(operationId, targetSource, ct).ConfigureAwait(false);
        RecordPlan(initial.Plan);
        if (!initial.Plan.CanRun) throw new DefaultRouteTransitionRejectedException(initial.Plan);

        await using var change = await authority.BeginChange(operationId, initial.TargetPlan, ct).ConfigureAwait(false);
        try
        {
            var prepared = await Prepare(operationId, targetSource, ct).ConfigureAwait(false);
            if (!prepared.Plan.CanRun) throw new DefaultRouteTransitionRejectedException(prepared.Plan);
            if (prepared.Plan.Source.RouteIdentity != change.Expected.Plan.RouteIdentity ||
                prepared.Plan.Source.ContentGeneration != change.Expected.ContentGeneration)
                throw new InvalidOperationException("The active route changed while the promotion was entering its serialized boundary.");

            var sourceBinding = authority.Bind(change.Expected.Plan, DataRouteOrigin.Default);
            var targetBinding = authority.Bind(change.Target, DataRouteOrigin.ExplicitSource);
            await using var maintenance = await horizon.CloseAndDrain(
                [
                    new DataRouteMaintenanceRequest(sourceBinding, BlockReads: false, BlockWrites: true),
                    new DataRouteMaintenanceRequest(targetBinding, BlockReads: true, BlockWrites: true, AllowQuarantined: true)
                ],
                ct).ConfigureAwait(false);

            // Inspection is deliberately repeated after host-mediated mutation admission is closed.
            prepared = await Prepare(operationId, targetSource, ct).ConfigureAwait(false);
            if (!prepared.Plan.CanRun) throw new DefaultRouteTransitionRejectedException(prepared.Plan);

            await change.MarkPending(ct).ConfigureAwait(false);
            Record(
                Infrastructure.Constants.FactCodes.Pending,
                KoanFactKind.Guarantee,
                KoanFactState.Observed,
                $"data:cutover:{targetSource}",
                $"Promotion '{operationId}' persisted its pending intent before target mutation.",
                "durable-pending-intent",
                "If the host stops before activation, inspect and reprovision the quarantined target before retrying.",
                operationId);
            var started = DateTimeOffset.UtcNow;
            var receipts = new List<DefaultRouteEntityReceipt>();
            var targetMarked = false;
            foreach (var root in manifest.Roots.Where(static root => root.IsEligible))
            {
                ct.ThrowIfCancellationRequested();
                var planned = prepared.Entities[root.RootIdentity];
                var source = root.Accessor!.Open(prepared.SourceFactory, change.Expected.Plan.Source);
                var target = root.Accessor.Open(prepared.TargetFactory, change.Target.Source);

                if (!targetMarked)
                {
                    await change.MarkTargetMutated(ct).ConfigureAwait(false);
                    targetMarked = true;
                }
                await target.EnsureReady(ct).ConfigureAwait(false);

                var timer = Stopwatch.StartNew();
                var copyDigest = await Copy(root, source, target, planned.SourceContainerPresent, ct).ConfigureAwait(false);
                var verified = await Verify(root, source, target, planned.SourceContainerPresent, copyDigest, ct)
                    .ConfigureAwait(false);
                timer.Stop();
                var entityReceipt = new DefaultRouteEntityReceipt(
                    root.RootIdentity,
                    verified.Count,
                    verified.Digest,
                    timer.Elapsed);
                receipts.Add(entityReceipt);
                RecordEntityVerified(operationId, entityReceipt);
            }

            // Cancellation is intentionally no longer observed once durable activation begins.
            var active = await change.Commit().ConfigureAwait(false);
            var completed = DateTimeOffset.UtcNow;
            var transitionReceipt = new DefaultRouteTransitionReceipt(
                operationId,
                started,
                completed,
                Describe(change.Expected),
                Describe(active),
                receipts);
            Record(
                Infrastructure.Constants.FactCodes.Completed,
                KoanFactKind.Election,
                KoanFactState.Selected,
                "data:route:default",
                $"Promotion '{operationId}' activated source '{active.Plan.Source}' at revision " +
                $"{active.AuthorityRevision}, generation {active.ContentGeneration}.",
                "verified-durable-activation",
                null,
                operationId);
            return transitionReceipt;
        }
        catch (DefaultRouteTransitionRejectedException rejected)
        {
            RecordPlan(rejected.Plan);
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            RecordFailure(operationId, targetSource, change.TargetMayContainData, "caller-cancelled");
            throw;
        }
        catch (Exception error)
        {
            RecordFailure(operationId, targetSource, change.TargetMayContainData, "promotion-failed");
            throw new DefaultRouteTransitionException(
                operationId,
                targetSource,
                change.TargetMayContainData,
                error);
        }
    }

    private async Task<PreparedPlan> Prepare(string operationId, string targetSource, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSource);
        var current = authority.Current;
        var sourceFactory = providers.Find(current.Plan.Adapter)
            ?? throw new InvalidOperationException(
                $"Active Data adapter '{current.Plan.Adapter}' is no longer available.");
        var targetDefinition = sources.GetSource(targetSource)
            ?? throw new InvalidOperationException(
                $"Target source '{targetSource}' is not configured. Add Koan:Data:Sources:{targetSource} before host composition.");
        if (string.IsNullOrWhiteSpace(targetDefinition.Adapter))
            throw new InvalidOperationException(
                $"Target source '{targetSource}' must configure an exact Adapter.");
        var targetFactory = providers.Find(targetDefinition.Adapter)
            ?? throw new InvalidOperationException(
                $"Target source '{targetSource}' requests unavailable adapter '{targetDefinition.Adapter}'.");
        var targetProvider = providers.Describe(targetFactory).Id;
        var targetPlan = sources.GetPlan(targetDefinition.Name, targetProvider);
        var blockers = new List<DefaultRouteTransitionBlocker>();

        if (_options.WriterOwnership != CutoverWriterOwnership.HostExclusiveOrExternallyQuiesced)
            blockers.Add(Block(
                Infrastructure.Constants.FailureCodes.WriterOwnership,
                "host",
                "Writer ownership has not been asserted for this cutover.",
                "Set Koan:Data:Cutover:WriterOwnership=HostExclusiveOrExternallyQuiesced only after external writers are absent or paused."));
        if (string.Equals(current.Plan.RouteIdentity, targetPlan.RouteIdentity, StringComparison.Ordinal) ||
            string.Equals(current.Plan.ConnectionIdentity, targetPlan.ConnectionIdentity, StringComparison.Ordinal))
            blockers.Add(Block(
                Infrastructure.Constants.FailureCodes.TargetPolicy,
                targetPlan.Source,
                "The target is not physically distinct from the active default.",
                "Configure a dedicated target database with a different connection identity."));
        if (targetPlan.StorageLifecycle != StorageLifecycle.Managed || targetPlan.Access != DataSourceAccess.ReadWrite)
            blockers.Add(Block(
                Infrastructure.Constants.FailureCodes.TargetPolicy,
                targetPlan.Source,
                "The target is not Managed + ReadWrite.",
                "Configure StorageLifecycle=Managed and Access=ReadWrite for the dedicated target."));

        var included = manifest.Roots.Where(static root => root.RouteScope == DataEntityRouteScope.Default).ToArray();
        if (included.Length == 0)
            blockers.Add(Block(
                Infrastructure.Constants.FailureCodes.ManifestEmpty,
                "application",
                "The compiled application manifest contains no default-routed Entity roots.",
                "Ensure concrete Entity models are source-discoverable before running a default-route promotion."));

        SourceInspection sourceInspection = SourceInspection.Unavailable("inspection-not-run");
        SourceInspection targetInspection = SourceInspection.Unavailable("inspection-not-run");
        var sourceIntegration = sourceFactory as IDataSourceIntegrationFactory;
        var targetIntegration = targetFactory as IDataSourceIntegrationFactory;
        if (sourceIntegration is not null && targetIntegration is not null)
        {
            sourceInspection = await Inspect(sourceIntegration, current.Plan, ct).ConfigureAwait(false);
            targetInspection = await Inspect(targetIntegration, targetPlan, ct).ConfigureAwait(false);
        }
        else
        {
            blockers.Add(Block(
                Infrastructure.Constants.FailureCodes.ProviderEnvelope,
                targetPlan.Source,
                "Both routes must expose complete source inspection.",
                "Use adapters implementing IDataSourceIntegrationFactory and IDataSourceStatusInspector."));
        }

        if (sourceInspection.Status.Status != DataSourceStorageStatus.Ready)
            blockers.Add(Block(
                Infrastructure.Constants.FailureCodes.SourceUnavailable,
                current.Plan.Source,
                $"The active source status is '{sourceInspection.Status.Status}' ({sourceInspection.Status.DetailCode}).",
                "Restore readable source storage before planning promotion."));
        if (targetInspection.Status.Status == DataSourceStorageStatus.Unavailable)
            blockers.Add(Block(
                Infrastructure.Constants.FailureCodes.TargetUnavailable,
                targetPlan.Source,
                $"The target status is unavailable ({targetInspection.Status.DetailCode}).",
                "Correct target connectivity, locking, or integrity before promotion."));
        if (targetInspection.Containers.Count != 0)
            blockers.Add(Block(
                Infrastructure.Constants.FailureCodes.TargetNotEmpty,
                targetPlan.Source,
                $"The target contains {targetInspection.Containers.Count} user container(s).",
                "Use a new dedicated target or empty/reprovision the quarantined target before retrying."));

        var knownSourceContainers = new List<StorageAddress>();
        var sourceContainersByRoot = new HashSet<string>(StringComparer.Ordinal);
        if (sourceIntegration is not null && sourceInspection.Status.Status == DataSourceStorageStatus.Ready)
        {
            foreach (var root in manifest.Roots)
            {
                if (root.RouteScope != DataEntityRouteScope.Default || root.Accessor is null) continue;
                var resolved = await ResolveExpectedContainer(
                        sourceIntegration,
                        current.Plan,
                        root.Accessor.ExpectedContainer(sourceFactory),
                        ct)
                    .ConfigureAwait(false);
                if (resolved is null) continue;
                knownSourceContainers.Add(resolved);
                sourceContainersByRoot.Add(root.RootIdentity);
            }
        }
        foreach (var container in sourceInspection.Containers)
        {
            if (!knownSourceContainers.Any(expected => SameAddress(expected, container.Address)) ||
                (container.Traits & (StorageContainerTraits.Records | StorageContainerTraits.Physical)) !=
                (StorageContainerTraits.Records | StorageContainerTraits.Physical))
                blockers.Add(Block(
                    Infrastructure.Constants.FailureCodes.SourceInventory,
                    container.DisplayPath,
                    "The active database contains an unexplained or non-physical user container.",
                    "Move unrelated storage out of the dedicated database or graduate an explicit physical-slice inventory."));
        }

        var publicEntities = new List<DefaultRouteEntityPlan>(manifest.Roots.Count);
        var byRoot = new Dictionary<string, DefaultRouteEntityPlan>(StringComparer.Ordinal);
        foreach (var root in manifest.Roots)
        {
            var rootBlockers = new List<DefaultRouteTransitionBlocker>();
            if (root.RouteScope == DataEntityRouteScope.Default)
            {
                rootBlockers.AddRange(root.Blockers.Select(blocker => Block(
                    blocker.Code,
                    root.RootIdentity,
                    blocker.Reason,
                    blocker.Correction)));
                if (mappings.Find(current.Plan.Source, root.RootType) is not null ||
                    mappings.Find(targetPlan.Source, root.RootType) is not null)
                    rootBlockers.Add(Block(
                        Infrastructure.Constants.FailureCodes.Mapping,
                        root.RootIdentity,
                        "The root has an explicit compatibility mapping on the source or target.",
                        "Remove the mapping or graduate a mapping-aware canonical migration envelope."));
                if (root.Accessor is not null &&
                    !root.Accessor.SupportsBoundedTraversal(sourceFactory, current.Plan.Source))
                    rootBlockers.Add(Block(
                        Infrastructure.Constants.FailureCodes.ProviderEnvelope,
                        root.RootIdentity,
                        $"Source adapter '{current.Plan.Adapter}' does not expose provider-bounded structured paging.",
                        "Use a source adapter graduated for bounded verified cutover traversal."));
                if (root.Accessor is not null &&
                    !root.Accessor.SupportsBoundedTraversal(targetFactory, targetPlan.Source))
                    rootBlockers.Add(Block(
                        Infrastructure.Constants.FailureCodes.ProviderEnvelope,
                        root.RootIdentity,
                        $"Target adapter '{targetPlan.Adapter}' does not expose provider-bounded structured paging.",
                        "Use a target adapter graduated for bounded verified cutover traversal."));
                blockers.AddRange(rootBlockers);
            }

            var sourceContainer = root.Accessor?.ExpectedContainer(sourceFactory) ?? string.Empty;
            var targetContainer = root.Accessor?.ExpectedContainer(targetFactory) ?? string.Empty;
            var entityPlan = new DefaultRouteEntityPlan(
                root.RootIdentity,
                root.RootType.FullName ?? root.RootType.Name,
                root.RouteScope == DataEntityRouteScope.OutsideDefault
                    ? DefaultRouteEntityDisposition.OutsideDefault
                    : rootBlockers.Count == 0
                        ? DefaultRouteEntityDisposition.Included
                        : DefaultRouteEntityDisposition.Rejected,
                sourceContainer,
                targetContainer,
                sourceContainersByRoot.Contains(root.RootIdentity),
                rootBlockers);
            publicEntities.Add(entityPlan);
            byRoot.Add(root.RootIdentity, entityPlan);
        }

        var plan = new DefaultRouteTransitionPlan(
            operationId,
            DateTimeOffset.UtcNow,
            Describe(current),
            Describe(targetPlan, current.ContentGenerations.GetValueOrDefault(targetPlan.RouteIdentity)),
            publicEntities,
            blockers);
        return new PreparedPlan(plan, sourceFactory, targetFactory, targetPlan, byRoot);
    }

    private async Task<SourceInspection> Inspect(
        IDataSourceIntegrationFactory factory,
        DataSourcePlan plan,
        CancellationToken ct)
    {
        var integration = factory.CreateSource(services, plan.Source);
        try
        {
            var inspector = integration.Inspector;
            if (inspector?.Native is not IDataSourceStatusInspector statusInspector)
                return SourceInspection.Unavailable("status-inspection-unsupported");
            var status = await statusInspector.Status(ct).ConfigureAwait(false);
            if (status.Status != DataSourceStorageStatus.Ready)
                return new SourceInspection(status, []);
            const SourceInspectionCapabilities required =
                SourceInspectionCapabilities.ListContainers | SourceInspectionCapabilities.ResolveAddress;
            if ((inspector.Capabilities & required) != required)
                return SourceInspection.Unavailable("container-inspection-unsupported");

            var containers = new List<StorageContainerDescriptor>();
            string? continuation = null;
            do
            {
                var batch = await inspector.Containers(_options.ContainerPageSize, continuation, ct).ConfigureAwait(false);
                if (batch.Containers.Count > _options.ContainerPageSize ||
                    batch.Completion == StorageContainerPageCompletion.MoreAvailable &&
                    string.IsNullOrWhiteSpace(batch.ProviderContinuation))
                    return SourceInspection.Unavailable("container-inspection-invalid");
                if (batch.Completion == StorageContainerPageCompletion.ProviderLimit)
                    return SourceInspection.Unavailable("container-inspection-provider-limit");
                containers.AddRange(batch.Containers);
                if (containers.Count > _options.MaximumContainers)
                    return SourceInspection.Unavailable("container-inventory-limit");
                continuation = batch.Completion == StorageContainerPageCompletion.MoreAvailable
                    ? batch.ProviderContinuation
                    : null;
            } while (continuation is not null);
            return new SourceInspection(status, containers);
        }
        finally
        {
            if (integration is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (integration is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private async Task<StorageAddress?> ResolveExpectedContainer(
        IDataSourceIntegrationFactory factory,
        DataSourcePlan plan,
        string container,
        CancellationToken ct)
    {
        var integration = factory.CreateSource(services, plan.Source);
        try
        {
            var inspector = integration.Inspector;
            if (inspector is null ||
                (inspector.Capabilities & SourceInspectionCapabilities.ResolveAddress) == 0)
                return null;
            try
            {
                return (await inspector.Resolve(StorageAddress.From(container), ct).ConfigureAwait(false)).Address;
            }
            catch (KeyNotFoundException)
            {
                return null;
            }
        }
        finally
        {
            if (integration is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else if (integration is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private static bool SameAddress(StorageAddress left, StorageAddress right)
    {
        if (!string.Equals(left.Name, right.Name, StringComparison.Ordinal) ||
            left.Namespace.Count != right.Namespace.Count)
            return false;
        for (var index = 0; index < left.Namespace.Count; index++)
            if (!string.Equals(left.Namespace[index], right.Namespace[index], StringComparison.Ordinal))
                return false;
        return true;
    }

    private async Task<DigestResult> Copy(
        DataEntityRootPlan root,
        IDataEntityRootSession source,
        IDataEntityRootSession target,
        bool sourcePresent,
        CancellationToken ct)
    {
        using var digest = new LogicalDigest();
        if (!sourcePresent) return new DigestResult(0, digest.Finish());
        var page = 1;
        while (true)
        {
            var batch = await source.ReadPage(page, _options.PageSize, ct).ConfigureAwait(false);
            foreach (var entity in batch.Items) digest.Append(CanonicalEntityWriter.Write(root.RootIdentity, entity));
            if (batch.Items.Count != 0) await target.Upsert(batch.Items, ct).ConfigureAwait(false);
            if (!batch.HasMore) break;
            page = NextPage(page);
        }
        return new DigestResult(digest.Count, digest.Finish());
    }

    private async Task<DigestResult> Verify(
        DataEntityRootPlan root,
        IDataEntityRootSession source,
        IDataEntityRootSession target,
        bool sourcePresent,
        DigestResult copied,
        CancellationToken ct)
    {
        using var sourceDigest = new LogicalDigest();
        var page = 1;
        while (true)
        {
            var sourcePage = sourcePresent
                ? await source.ReadPage(page, _options.PageSize, ct).ConfigureAwait(false)
                : new DataEntityPage([], false);
            var sourceIds = sourcePage.Items
                .Select(static entity => ((IEntity<string>)entity).Id)
                .ToArray();
            var targetItems = await target.ReadByIds(sourceIds, ct).ConfigureAwait(false);
            if (sourcePage.Items.Count != targetItems.Count)
                throw Verification(root, $"page {page} returned an invalid identity-batch receipt");
            for (var index = 0; index < sourcePage.Items.Count; index++)
            {
                var sourceEntity = sourcePage.Items[index];
                var targetEntity = targetItems[index]
                    ?? throw Verification(root, $"record {index} on page {page} is missing from the target");
                var sourceId = ((IEntity<string>)sourceEntity).Id;
                var targetId = ((IEntity<string>)targetEntity).Id;
                var sourceBytes = CanonicalEntityWriter.Write(root.RootIdentity, sourceEntity);
                var targetBytes = CanonicalEntityWriter.Write(root.RootIdentity, targetEntity);
                if (!string.Equals(sourceId, targetId, StringComparison.Ordinal) ||
                    !sourceBytes.AsSpan().SequenceEqual(targetBytes))
                    throw Verification(root, $"record {index} on page {page} differs by identity, runtime type, or logical value");
                sourceDigest.Append(sourceBytes);
            }
            if (!sourcePage.HasMore) break;
            page = NextPage(page);
        }

        var reread = new DigestResult(sourceDigest.Count, sourceDigest.Finish());
        if (copied != reread)
            throw Verification(root, "copy and stable source reread digests do not agree");
        var targetCount = await CountRows(target, ct).ConfigureAwait(false);
        if (targetCount != reread.Count)
            throw Verification(root, $"source cardinality {reread.Count} differs from target cardinality {targetCount}");
        return reread;
    }

    private async Task<long> CountRows(IDataEntityRootSession session, CancellationToken ct)
    {
        long count = 0;
        var page = 1;
        while (true)
        {
            var batch = await session.ReadPage(page, _options.PageSize, ct).ConfigureAwait(false);
            count = checked(count + batch.Items.Count);
            if (!batch.HasMore) return count;
            page = NextPage(page);
        }
    }

    private static InvalidDataException Verification(DataEntityRootPlan root, string reason)
        => new($"Verified cutover rejected Entity root '{root.RootIdentity}': {reason}. The target is quarantined.");

    private static int NextPage(int page)
        => page == int.MaxValue
            ? throw new InvalidOperationException("Cutover paging exceeded the supported provider page range.")
            : page + 1;

    private static DefaultRouteTransitionBlocker Block(
        string code,
        string subject,
        string reason,
        string correction)
        => new(code, subject, reason, correction);

    private void RecordPlan(DefaultRouteTransitionPlan plan)
    {
        var first = plan.Blockers.FirstOrDefault();
        Record(
            plan.CanRun ? Infrastructure.Constants.FactCodes.Planned : Infrastructure.Constants.FactCodes.Rejected,
            plan.CanRun ? KoanFactKind.Guarantee : KoanFactKind.Rejection,
            plan.CanRun ? KoanFactState.Selected : KoanFactState.Rejected,
            $"data:cutover:{plan.Target.Source}",
            plan.CanRun
                ? $"Promotion '{plan.OperationId}' is within the verified cutover envelope for {plan.Entities.Count} Entity root(s)."
                : $"Promotion '{plan.OperationId}' was rejected by {plan.Blockers.Count} blocker(s).",
            first?.Code ?? "verified-preflight",
            first?.Correction,
            plan.OperationId);
    }

    private void RecordEntityVerified(string operationId, DefaultRouteEntityReceipt receipt)
        => Record(
            Infrastructure.Constants.FactCodes.EntityVerified,
            KoanFactKind.Guarantee,
            KoanFactState.Healthy,
            $"data:entity:{receipt.RootIdentity}",
            $"Verified {receipt.Count} logical record(s) with digest {receipt.Digest} in {receipt.Duration.TotalMilliseconds:F0} ms.",
            "canonical-source-target-match",
            null,
            $"{operationId}:{receipt.RootIdentity}");

    private void RecordFailure(
        string operationId,
        string targetSource,
        bool targetMayContainData,
        string reason)
        => Record(
            Infrastructure.Constants.FactCodes.Failed,
            KoanFactKind.Degradation,
            KoanFactState.CollectionFailed,
            $"data:cutover:{targetSource}",
            $"Promotion '{operationId}' failed before activation; the active route is unchanged" +
            (targetMayContainData ? " and the target is quarantined." : "."),
            reason,
            targetMayContainData
                ? "Empty or reprovision the target, then plan again."
                : "Correct the reported failure, then plan again.",
            operationId);

    private void Record(
        string code,
        KoanFactKind kind,
        KoanFactState state,
        string subject,
        string summary,
        string reason,
        string? correction,
        string correlation)
        => services.GetService<IKoanRuntimeFactRecorder>()?.Record(new KoanFactDescriptor(
            code,
            kind,
            state,
            subject,
            summary,
            reason,
            correction,
            "Koan.Data.Cutover",
            correlation));

    private static DefaultRouteDescriptor Describe(DefaultDataRouteSnapshot snapshot)
        => Describe(snapshot.Plan, snapshot.ContentGeneration);

    private static DefaultRouteDescriptor Describe(DataSourcePlan plan, long generation)
        => new(plan.Source, plan.Adapter, plan.RouteIdentity, plan.ConnectionIdentity, generation);

    private static DataCutoverOptions Validate(DataCutoverOptions options)
    {
        if (options.PageSize <= 0) throw new InvalidOperationException("Koan:Data:Cutover:PageSize must be positive.");
        if (options.ContainerPageSize <= 0)
            throw new InvalidOperationException("Koan:Data:Cutover:ContainerPageSize must be positive.");
        if (options.MaximumContainers <= 0)
            throw new InvalidOperationException("Koan:Data:Cutover:MaximumContainers must be positive.");
        return options;
    }

    private sealed record PreparedPlan(
        DefaultRouteTransitionPlan Plan,
        IDataAdapterFactory SourceFactory,
        IDataAdapterFactory TargetFactory,
        DataSourcePlan TargetPlan,
        IReadOnlyDictionary<string, DefaultRouteEntityPlan> Entities);

    private sealed record SourceInspection(
        DataSourceStorageState Status,
        IReadOnlyList<StorageContainerDescriptor> Containers)
    {
        internal static SourceInspection Unavailable(string detail)
            => new(new DataSourceStorageState(DataSourceStorageStatus.Unavailable, detail), []);
    }

    private sealed record DigestResult(long Count, string Digest);
}
