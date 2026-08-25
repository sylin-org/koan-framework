using Koan.Canon;
using Koan.Canon;
using Koan.Canon.Internal;

namespace Koan.Tests.Canon.Unit.Specs.Runtime;

public sealed class CanonIntakeSpec
{
    [Fact]
    public async Task Model_override_normalizes_before_canonical_persistence()
    {
        var persistence = new RecordingPersistence();
        var runtime = Runtime(persistence);

        var result = await runtime.Canonize(new OnboardCanon { Email = "  Alice@Example.COM " });

        ((OnboardCanon)persistence.LastCanonical!).Email.Should().Be("alice@example.com");
    }

    [Fact]
    public async Task Gateway_rules_run_after_model_override_in_registration_order()
    {
        Person.Canon.Reset();
        var order = new List<string>();
        var modelCalls = new List<string>();
        var persistence = new RecordingPersistence();
        var runtime = Runtime(persistence);
        Person.Canon.OnIntake(p => { order.Add("rule-1"); return p; });
        Person.Canon.OnIntake(p => { order.Add("rule-2"); return p; });

        // The model override records its own pass through a static side channel.
        Person.OverrideOrder = modelCalls;

        await runtime.Canonize(new Person { Email = "p@example.com" });

        // The model override speaks first, then the gateway rules in registration order.
        modelCalls.Should().Equal("model");
        order.Should().Equal("rule-1", "rule-2");
    }

    [Fact]
    public async Task Mutation_style_action_rule_adjusts_fields()
    {
        Person.Canon.Reset();
        Person.Canon.OnIntake(p => p.Phone = p.Phone?.Trim());
        var persistence = new RecordingPersistence();
        var runtime = Runtime(persistence);

        await runtime.Canonize(new Person { Email = "p@example.com", Phone = " +14155550123 " });

        var persisted = persistence.LastCanonical as Person;
        persisted.Should().NotBeNull();
        persisted!.Phone.Should().Be("+14155550123");
    }

    [Fact]
    public async Task Null_return_from_gateway_rule_fails_correctively()
    {
        Person.Canon.Reset();
        Person.Canon.OnIntake(_ => null!);
        var runtime = Runtime(new RecordingPersistence());

        var error = (await runtime.Awaiting(r => r.Canonize(new Person()))
            .Should().ThrowAsync<InvalidOperationException>()).Which;

        error.Message.Should().Contain("returned null").And.Contain("Failed event");
    }

    [Fact]
    public async Task Different_instance_from_gateway_rule_fails_correctively()
    {
        Person.Canon.Reset();
        Person.Canon.OnIntake(_ => new Person());
        var runtime = Runtime(new RecordingPersistence());

        var error = (await runtime.Awaiting(r => r.Canonize(new Person()))
            .Should().ThrowAsync<InvalidOperationException>()).Which;

        error.Message.Should().Contain("different instance").And.Contain("in place");
    }

    [Fact]
    public async Task Reset_clears_registered_rules()
    {
        Person.Canon.Reset();
        Person.Canon.OnIntake(_ => throw new InvalidOperationException("should not run"));
        Person.Canon.Reset();

        var act = () => Runtime(new RecordingPersistence()).Canonize(new Person { Email = "p@example.com" });

        await act.Should().NotThrowAsync();
    }

    private static CanonRuntime Runtime(RecordingPersistence persistence)
    {
        var builder = new CanonRuntimeBuilder().UsePersistence(persistence);
        builder.ConfigurePipeline<OnboardCanon>(pipeline => { });
        builder.ConfigurePipeline<Person>(pipeline => { });
        return (CanonRuntime)builder.Build();
    }

    private sealed class RecordingPersistence : ICanonPersistence
    {
        public object? LastCanonical { get; private set; }

        public Task<TModel?> GetCanonicalAsync<TModel>(string canonicalId, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
            => Task.FromResult<TModel?>(null);

        public Task<TModel> PersistCanonicalAsync<TModel>(TModel entity, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
        {
            LastCanonical = entity;
            return Task.FromResult(entity);
        }

        public Task<CanonStage<TModel>> PersistStageAsync<TModel>(CanonStage<TModel> stage, CancellationToken cancellationToken)
            where TModel : CanonEntity<TModel>, new()
            => Task.FromResult(stage);

        public Task<CanonIndex?> GetIndex(string entityType, string key, CancellationToken cancellationToken)
            => Task.FromResult<CanonIndex?>(null);

        public Task UpsertIndex(CanonIndex index, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    public sealed class OnboardCanon : CanonEntity<OnboardCanon>
    {
        [MatchKey]
        public string Email { get; set; } = "";

        public override OnboardCanon OnIntake(OnboardCanon candidate)
        {
            candidate.Email = candidate.Email.Trim().ToLowerInvariant();
            return candidate;
        }
    }

    public sealed class Person : CanonEntity<Person>
    {
        [MatchKey]
        public string Email { get; set; } = "";

        public string? Phone { get; set; }

        /// Static side-channel for ordering assertions without coupling to rule closures.
        public static List<string>? OverrideOrder { get; set; }

        public override Person OnIntake(Person candidate)
        {
            OverrideOrder?.Add("model");
            return base.OnIntake(candidate);
        }
    }
}
