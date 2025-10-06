using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Canon.Domain.Metadata;
using Koan.Canon.Domain.Runtime;
using Koan.Core.Hosting.App;
using Koan.Data.Core.Model;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Canon.Domain.Model;

/// <summary>
/// Base type for canonical aggregates. Extends the Koan entity base while tracking canon metadata and lifecycle.
/// </summary>
/// <typeparam name="TModel">Concrete canonical entity type.</typeparam>
public abstract class CanonEntity<TModel> : Entity<TModel>
    where TModel : CanonEntity<TModel>, new()
{
    private CanonMetadata _metadata = new();
    private CanonState _state = CanonState.Default;

    /// <summary>
    /// Canon metadata describing sources, policies, and lineage for this entity.
    /// </summary>
    public CanonMetadata Metadata
    {
        get => _metadata;
        set
        {
            _metadata = value ?? new CanonMetadata();
            _state = _metadata.State.Copy();
        }
    }

    /// <summary>
    /// Current canon state combining lifecycle, readiness, and consumer signals.
    /// </summary>
    public CanonState State
    {
        get => _state;
        private set
        {
            _state = value ?? CanonState.Default;
            _metadata.State = _state.Copy();
            _metadata.Touch();
        }
    }

    /// <summary>
    /// Convenience accessor for the lifecycle component of <see cref="State"/>.
    /// </summary>
    public CanonLifecycle Lifecycle => State.Lifecycle;

    /// <summary>
    /// Sets the canon state. Prefer helper methods such as <see cref="ApplyState"/> for incremental updates.
    /// </summary>
    /// <param name="state">State to apply.</param>
    public void SetState(CanonState state)
    {
        State = state ?? CanonState.Default;
    }

    /// <summary>
    /// Applies a transformation to the current state.
    /// </summary>
    /// <param name="transform">Transformation delegate.</param>
    public void ApplyState(Func<CanonState, CanonState> transform)
    {
        if (transform is null)
        {
            throw new ArgumentNullException(nameof(transform));
        }

        State = transform(State);
    }

    /// <summary>
    /// Ensures metadata remains aligned with the canonical identifier.
    /// </summary>
    public override string Id
    {
        get
        {
            var id = base.Id;
            if (!Metadata.HasCanonicalId)
            {
                Metadata.AssignCanonicalId(id);
            }

            return id;
        }
        set
        {
            base.Id = value;
            Metadata.AssignCanonicalId(value);
        }
    }

    /// <summary>
    /// Marks the entity as superseded by another canonical identifier.
    /// </summary>
    /// <param name="replacementId">Replacement canonical identifier.</param>
    /// <param name="reason">Optional reason for the supersession.</param>
    public void MarkSuperseded(string replacementId, string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(replacementId))
        {
            throw new ArgumentException("Replacement identifier must be provided.", nameof(replacementId));
        }

        ApplyState(static state => state.WithLifecycle(CanonLifecycle.Superseded));
        Metadata.Lineage.MarkSupersededBy(replacementId, reason);
    }

    /// <summary>
    /// Marks the entity as archived.
    /// </summary>
    /// <param name="notes">Optional archival notes.</param>
    public void Archive(string? notes = null)
    {
        ApplyState(static state => state.WithLifecycle(CanonLifecycle.Archived));
        if (!string.IsNullOrWhiteSpace(notes))
        {
            Metadata.SetTag("archive:notes", notes);
        }
    }

    /// <summary>
    /// Restores the entity to an active state, clearing archival notes if present.
    /// </summary>
    public void Restore()
    {
        ApplyState(static state => state.WithLifecycle(CanonLifecycle.Active));
        Metadata.RemoveTag("archive:notes");
    }

    /// <summary>
    /// Flags the entity as withdrawn.
    /// </summary>
    /// <param name="reason">Reason for withdrawal.</param>
    public void Withdraw(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Withdrawal reason must be provided.", nameof(reason));
        }

        ApplyState(static state => state.WithLifecycle(CanonLifecycle.Withdrawn));
        Metadata.SetTag("withdrawn:reason", reason);
    }

    /// <summary>
    /// Canonizes the current entity using the globally registered <see cref="ICanonRuntime"/>.
    /// </summary>
    /// <param name="origin">Optional origin identifier applied to the canonization request.</param>
    /// <param name="configure">Optional delegate to customize <see cref="CanonizationOptions"/>.</param>
    /// <param name="cancellationToken">Cancellation token for the canonization operation.</param>
    public Task<CanonizationResult<TModel>> Canonize(
        string? origin = null,
        Func<CanonizationOptions, CanonizationOptions>? configure = null,
        CancellationToken cancellationToken = default)
    {
        var runtime = ResolveRuntime();

        CanonizationOptions? options = null;

        if (!string.IsNullOrWhiteSpace(origin))
        {
            options = (options ?? CanonizationOptions.Default).WithOrigin(origin!);
        }

        if (configure is not null)
        {
            var baseOptions = options ?? CanonizationOptions.Default;
            var configured = configure(baseOptions.Copy())
                ?? throw new InvalidOperationException("Canonization options delegate must return an instance.");
            options = configured;
        }

        return runtime.Canonize((TModel)this, options, cancellationToken);
    }

    private static ICanonRuntime ResolveRuntime()
    {
        var services = AppHost.Current ?? throw new InvalidOperationException("AppHost.Current is not set. Ensure builder.Services.AddKoan() executes during startup.");
        var runtime = services.GetService<ICanonRuntime>();
        if (runtime is null)
        {
            throw new InvalidOperationException("ICanonRuntime is not registered. Ensure builder.Services.AddKoan() wires Canon runtime services.");
        }

        return runtime;
    }
}
