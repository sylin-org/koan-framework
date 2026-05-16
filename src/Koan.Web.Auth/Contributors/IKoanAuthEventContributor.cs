namespace Koan.Web.Auth.Contributors;

/// <summary>
/// Pluggable hook into the platform auth lifecycle. Auto-discovered by reflection — implementations
/// do not need to be registered with DI. Each event runs the full pipeline of contributors exactly
/// once in <see cref="Priority"/> order.
/// </summary>
/// <remarks>
/// <para>
/// <b>Priority</b> determines dispatch order within a single event invocation. Identity-mapping
/// work (e.g. mapping a provider <c>sub</c> to a platform user id) belongs at very low priorities
/// (<see cref="int.MinValue"/>) so it runs before role attribution, audit, and other downstream
/// work. Built-in framework contributors run at default priority (0); one-shot bootstrap
/// elevations (<see cref="Builtin.AdminBootstrapContributor"/>) run at higher priorities so they
/// observe what other contributors stamped.
/// </para>
/// <para>
/// <b>Failure semantics:</b> exceptions thrown by a contributor are logged and swallowed by the
/// dispatcher; the next contributor still runs. The only exception is
/// <see cref="System.OperationCanceledException"/>, which must propagate (host shutdown / client
/// disconnect). Contributors that need to abort a sign-in should call
/// <see cref="AuthSignInContext.Reject(string)"/> rather than throw.
/// </para>
/// <para>
/// <b>Lifecycle event coverage:</b> all three methods default to a no-op. Contributors override
/// only the events they care about.
/// </para>
/// </remarks>
public interface IKoanAuthEventContributor
{
    /// <summary>Lower values run first. Default 0. Identity-mapping uses <see cref="int.MinValue"/>.</summary>
    int Priority => 0;

    /// <summary>
    /// Runs once at application startup via the framework's bootstrap hosted service. Use this
    /// for set-wide reconciliation: backfilling role state, seeding initial entities, reconciling
    /// against external sources. Contributors that need to iterate users should use Koan
    /// <c>Entity&lt;T&gt;</c> statics directly (e.g. <c>await User.All(ct)</c>); the framework
    /// does not abstract over the application's user entity shape.
    /// </summary>
    System.Threading.Tasks.Task OnBootstrap(AuthBootstrapContext ctx, System.Threading.CancellationToken ct)
        => System.Threading.Tasks.Task.CompletedTask;

    /// <summary>
    /// Runs during sign-in, AFTER the platform user id has been resolved by identity-mapping
    /// contributors. Mutate <see cref="AuthSignInContext.Identity"/> to bake claims into the
    /// cookie. Call <see cref="AuthSignInContext.Reject(string)"/> to short-circuit the pipeline
    /// and signal the outer flow that this sign-in must not produce a usable session.
    /// </summary>
    System.Threading.Tasks.Task OnSignIn(AuthSignInContext ctx, System.Threading.CancellationToken ct)
        => System.Threading.Tasks.Task.CompletedTask;

    /// <summary>
    /// Runs during sign-out. Use for cleanup, audit emission, or per-user session-cache
    /// invalidation in downstream stores. The user id may be <see langword="null"/> if the
    /// outgoing cookie did not carry one.
    /// </summary>
    System.Threading.Tasks.Task OnSignOut(AuthSignOutContext ctx, System.Threading.CancellationToken ct)
        => System.Threading.Tasks.Task.CompletedTask;
}
