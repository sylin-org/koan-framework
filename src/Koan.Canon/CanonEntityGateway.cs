using System;

namespace Koan.Canon;

/// <summary>
/// Type-scoped canon surface for a closed <see cref="CanonEntity{TModel}"/>: reached as
/// <c>Person.Canon</c>. Holds the rules composition registers for this entity type - the
/// external complement to the model's own <see cref="CanonEntity{TModel}.OnIntake"/> override.
/// Rules run after the model's override, in registration order, before user Validation contributors.
/// </summary>
public sealed class CanonEntityGateway<TModel>
    where TModel : CanonEntity<TModel>, new()
{
    private readonly object _gate = new();
    private List<Func<TModel, TModel>> _intakeRules = [];
    private List<Func<TModel, Task<string?>>> _businessRules = [];
    private List<Action<CanonizationResult<TModel>>> _committedHandlers = [];
    private List<Action<CanonizationResult<TModel>>> _parkedHandlers = [];
    private List<Action<CanonizationResult<TModel>>> _failedHandlers = [];

    /// <summary>
    /// Register a business rule. Runs at the business checkpoint (first occupant of Distribution,
    /// after the synthetic candidate is complete): return null to pass, or a justification string
    /// to hold the receipt as Refused. Rules run in registration order; first hold wins.
    /// </summary>
    public CanonEntityGateway<TModel> OnRule(Func<TModel, string?> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        lock (_gate)
        {
            _businessRules.Add(candidate => Task.FromResult(rule(candidate)));
        }

        return this;
    }

    /// <summary>Async business rule — same name, async by nature (a CRM lookup lives here).</summary>
    public CanonEntityGateway<TModel> OnRule(Func<TModel, Task<string?>> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        lock (_gate)
        {
            _businessRules.Add(rule);
        }

        return this;
    }

    /// <summary>
    /// Register an arrival-normalization rule. Mutate the candidate in place and return it;
    /// returning null, or a different instance, fails the operation correctively - exactly like
    /// the model's own override.
    /// </summary>
    public CanonEntityGateway<TModel> OnIntake(Func<TModel, TModel> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        lock (_gate)
        {
            _intakeRules.Add(rule);
        }

        return this;
    }

    /// <summary>
    /// Mutation-style overload for rules that adjust fields and return nothing.
    /// </summary>
    public CanonEntityGateway<TModel> OnIntake(Action<TModel> rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        lock (_gate)
        {
            _intakeRules.Add(candidate =>
            {
                rule(candidate);
                return candidate;
            });
        }

        return this;
    }

    /// <summary>Observes canonized arrivals: the canonical record and indexes are durable.</summary>
    public CanonEntityGateway<TModel> OnCommitted(Action<CanonizationResult<TModel>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            _committedHandlers.Add(handler);
        }

        return this;
    }

    /// <summary>Observes parked operations: the payload waits as a staged receipt.</summary>
    public CanonEntityGateway<TModel> OnParked(Action<CanonizationResult<TModel>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            _parkedHandlers.Add(handler);
        }

        return this;
    }

    /// <summary>Observes failed operations: nothing was committed.</summary>
    public CanonEntityGateway<TModel> OnFailed(Action<CanonizationResult<TModel>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        lock (_gate)
        {
            _failedHandlers.Add(handler);
        }

        return this;
    }

    /// <summary>
    /// The hold surface: scoreboard (`Counts`), triage (query the stage receipts directly), and
    /// the `Recover` verb. See `canon-rides-jobs.md` and the canon language document.
    /// </summary>
    public HoldGateway<TModel> Hold => new();

    internal IReadOnlyList<Func<TModel, Task<string?>>> RuleSnapshot()
    {
        lock (_gate)
        {
            return [.. _businessRules];
        }
    }

    internal TModel ApplyIntakeRules(TModel candidate)
    {
        Func<TModel, TModel>[] rules;
        lock (_gate)
        {
            rules = [.. _intakeRules];
        }

        foreach (var rule in rules)
        {
            var applied = rule(candidate)
                ?? throw new InvalidOperationException(
                    $"A canon intake rule for '{typeof(TModel).Name}' returned null. " +
                    "Return the candidate (transformed or not); rejection belongs to a Validation contributor emitting a Failed event.");

            if (!ReferenceEquals(applied, candidate))
            {
                throw new InvalidOperationException(
                    $"A canon intake rule for '{typeof(TModel).Name}' returned a different instance. " +
                    "Mutate the candidate in place and return it - the pipeline carries one arriving instance through its phases.");
            }
        }

        return candidate;
    }

    /// <summary>
    /// Invokes the observers registered for <paramref name="result"/>'s outcome. Runs after the
    /// commit checkpoints are done; handler exceptions surface to the caller after the durable
    /// state is already recorded.
    /// </summary>
    internal void RaiseOutcome(CanonizationResult<TModel> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        Action<CanonizationResult<TModel>>[] handlers;
        lock (_gate)
        {
            handlers = result.Outcome switch
            {
                CanonizationOutcome.Canonized => [.. _committedHandlers],
                CanonizationOutcome.Parked => [.. _parkedHandlers],
                CanonizationOutcome.Failed => [.. _failedHandlers],
                _ => []
            };
        }

        foreach (var handler in handlers)
        {
            handler(result);
        }
    }

    internal int IntakeRuleCount
    {
        get
        {
            lock (_gate)
            {
                return _intakeRules.Count;
            }
        }
    }

    /// <summary>Removes all registered rules and observers for this entity type. Intended for test isolation.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _intakeRules = [];
            _businessRules = [];
            _committedHandlers = [];
            _parkedHandlers = [];
            _failedHandlers = [];
        }
    }
}
