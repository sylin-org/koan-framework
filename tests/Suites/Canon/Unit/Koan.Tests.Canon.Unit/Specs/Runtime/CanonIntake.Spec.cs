using Koan.Canon;
using Koan.Canon;
using Koan.Canon.Internal;
using Koan.Data.Abstractions.Annotations;

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

    [Fact]
    public async Task Hygiene_annotations_normalize_arrival_without_a_manual_override()
    {
        // Intake parity with Data.Hygiene: declaring [Trim]/[Lowercase]/[Uppercase] on the model does at
        // intake what the persistence tier does at save — no hand-written OnIntake normalization required.
        AnnotatedCanon.Canon.Reset();
        var persistence = new RecordingPersistence();
        var runtime = new CanonRuntimeBuilder().UsePersistence(persistence)
            .ConfigurePipeline<AnnotatedCanon>(pipeline => { })
            .Build();

        await runtime.Canonize(new AnnotatedCanon
        {
            Email = "  Alice@Example.COM ",
            Code = " ship-7 ",
            Note = "  keep my padding  ",
        });

        var persisted = (AnnotatedCanon)persistence.LastCanonical!;
        persisted.Email.Should().Be("alice@example.com");
        persisted.Code.Should().Be("SHIP-7");
        persisted.Note.Should().Be("  keep my padding  ", "properties without hygiene annotations are never touched");
    }

    [Fact]
    public async Task Hygiene_runs_after_the_model_override_and_before_composition_rules()
    {
        // Composition rules observe hygiene-normalized values, and an explicit business rule keeps final say
        // over a declarative annotation.
        AnnotatedCanon.Canon.Reset();
        string? seenByRule = null;
        AnnotatedCanon.Canon.OnIntake(p =>
        {
            seenByRule = $"code={p.Code}|email={p.Email}";
            p.Code = p.Code.ToLowerInvariant();   // explicit rule overrides [Uppercase]
            return p;
        });

        var persistence = new RecordingPersistence();
        var runtime = new CanonRuntimeBuilder().UsePersistence(persistence)
            .ConfigurePipeline<AnnotatedCanon>(pipeline => { })
            .Build();

        await runtime.Canonize(new AnnotatedCanon { Email = " Alice@Example.COM ", Code = " ship-7 " });

        seenByRule.Should().Be("code=SHIP-7|email=alice@example.com",
            "hygiene sweeps between the model override and composition rules");
        ((AnnotatedCanon)persistence.LastCanonical!).Code.Should().Be("ship-7",
            "an explicit intake rule wins final say over the annotation");
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

    /// <summary>Declares hygiene annotations on identity-ish tokens but NO manual OnIntake normalization —
    /// the intake sweep must do the preparation (intake parity with Data.Hygiene).</summary>
    public sealed class AnnotatedCanon : CanonEntity<AnnotatedCanon>
    {
        [MatchKey]
        [Trim, Lowercase]
        public string Email { get; set; } = "";

        [Trim, Uppercase]
        public string Code { get; set; } = "";

        public string Note { get; set; } = "";
    }
}
