using AwesomeAssertions;
using Koan.Canon;
using Koan.Core;
using Koan.Jobs;
using Koan.Testing.Integration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Koan.Tests.Integration.Bootstrap.Pillars.Specs;

/// <summary>
/// The canon-rides-jobs golden journey: a deferred arrival rides the engine, a business rule holds
/// it as Refused at the business checkpoint, and recovery resubmits it — the corrective loop.
/// </summary>
public sealed class CanonStageEngineSpec
{
    private static Task<IntegrationHost> StartHostAsync()
        => PillarHost.Configure()
            .ConfigureServices(services => services.AddKoan())
            .StartAsync();

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return true;
            await Task.Delay(100);
        }

        return await condition();
    }

    [Fact]
    public async Task deferred_arrival_rides_the_engine_to_completed()
    {
        Poke.Canon.Reset();
        await using var host = await StartHostAsync();

        // PROBE
        var plan = host.Services.GetRequiredService<CanonCompositionPlan>();
        var registry = host.Services.GetRequiredService<JobTypeRegistry>();
        CanonModule.SeedStageJobs();
        var seeded = string.Join(",", Koan.Core.Hosting.Registry.KoanRegistry
            .GetDiscoveredImplementors(typeof(IKoanJob)).Select(t => t.Name));
        var probeModels = string.Join(",", plan.Models.Select(m => m.ModelType.Name));
        var probeBindings = string.Join(",", registry.All.Select(b => b.WorkType));

        var entity = new Poke { Name = "happy", Key = "k1" };
        CanonizationResult<Poke> result;
        try
        {
            result = await entity.Canonize(configure: o => o.WithStageBehavior(CanonStageBehavior.StageOnly));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"PROBE seeded=[{seeded}] models=[{probeModels}] bindings=[{probeBindings}]", ex);
        }
        result.Outcome.Should().Be(CanonizationOutcome.Parked);

        var staged = await CanonStage<Poke>.Query(s => s.Status == CanonStageStatus.Pending);
        var stage = staged.Should().ContainSingle().Subject;

        var completed = await WaitUntilAsync(
            async () => (await CanonStage<Poke>.Get(stage.Id))?.Status == CanonStageStatus.Completed,
            TimeSpan.FromSeconds(30));
        completed.Should().BeTrue($"PROBE seeded=[{seeded}] models=[{probeModels}] bindings=[{probeBindings}]");
    }

    [Fact]
    public async Task business_veto_holds_refused_at_the_business_checkpoint()
    {
        Poke.Canon.Reset();
        Poke.Canon.OnRule(_ => "user not found in CRM");
        await using var host = await StartHostAsync();

        await new Poke { Name = "vetoed", Key = "k2" }
            .Canonize(configure: o => o.WithStageBehavior(CanonStageBehavior.StageOnly));

        CanonStage<Poke>? held = null;
        var parked = await WaitUntilAsync(async () =>
        {
            var matches = await CanonStage<Poke>.Query(
                s => s.Status == CanonStageStatus.Parked && s.Reason == HoldReason.Refused);
            if (matches is [var found])
            {
                held = found;
                return true;
            }
            return false;
        }, TimeSpan.FromSeconds(30));

        parked.Should().BeTrue("the engine processes the receipt and the business rule holds it");
        held!.ParkedPhase.Should().Be(CanonPipelinePhase.Distribution);
        held.ErrorMessage.Should().Be("user not found in CRM");

        (await Poke.Canon.Hold.Counts.Refused()).Should().Be(1);
        (await Poke.Canon.Hold.Counts.All()).Should().Be(1);
    }

    [Fact]
    public async Task recovery_skip_and_resubmit_behave()
    {
        Poke.Canon.Reset();
        Poke.Canon.OnRule(_ => "user not found in CRM");
        await using var host = await StartHostAsync();

        await new Poke { Name = "stuck", Key = "k3" }
            .Canonize(configure: o => o.WithStageBehavior(CanonStageBehavior.StageOnly));
        await WaitUntilAsync(async () =>
            (await CanonStage<Poke>.Query(s => s.Status == CanonStageStatus.Parked)) is [var s] && s.Reason == HoldReason.Refused,
            TimeSpan.FromSeconds(30));
        var held = (await CanonStage<Poke>.Query(s => s.Status == CanonStageStatus.Parked)).Single();

        // A null decision leaves the record held.
        var skipped = await Poke.Canon.Hold.Recover(held.Id, _ => (HoldContext<Poke>?)null);
        skipped.Status.Should().Be(CanonStageStatus.Parked);

        // Blunt resubmission: the rule vetoes again — the corrective loop is proven, not assumed.
        var summary = await Poke.Canon.Hold.Recover();
        summary.Attempted.Should().Be(1);
        summary.Recovered.Should().Be(1);

        var reParked = await WaitUntilAsync(async () =>
            (await CanonStage<Poke>.Get(held.Id))?.Status == CanonStageStatus.Parked,
            TimeSpan.FromSeconds(30));
        reParked.Should().BeTrue("the rule still refuses the record; recovery re-parks it with its reason");
        (await CanonStage<Poke>.Get(held.Id))!.Reason.Should().Be(HoldReason.Refused);
    }

    [Fact]
    public async Task recovery_with_fix_reenters_and_completes()
    {
        Poke.Canon.Reset();
        Poke.Canon.OnRule(c => c.Name == "bad" ? "blocked while bad" : null);
        await using var host = await StartHostAsync();

        await new Poke { Name = "bad", Key = "k4" }
            .Canonize(configure: o => o.WithStageBehavior(CanonStageBehavior.StageOnly));
        await WaitUntilAsync(async () =>
            (await CanonStage<Poke>.Query(s => s.Status == CanonStageStatus.Parked)) is [var s] && s.Reason == HoldReason.Refused,
            TimeSpan.FromSeconds(30));
        var held = (await CanonStage<Poke>.Query(s => s.Status == CanonStageStatus.Parked)).Single();

        var outcome = await Poke.Canon.Hold.Recover(held.Id, i =>
        {
            i.Model.Name = "good";
            return i;
        });
        outcome.Status.Should().Be(CanonStageStatus.Pending);

        var completed = await WaitUntilAsync(
            async () => (await CanonStage<Poke>.Get(held.Id))?.Status == CanonStageStatus.Completed,
            TimeSpan.FromSeconds(30));
        completed.Should().BeTrue("the repaired record passes the rule and canonizes");
    }
}

public sealed class Poke : CanonEntity<Poke>
{
    public string Name { get; set; } = "";

    [MatchKey]
    public string Key { get; set; } = "";
}
