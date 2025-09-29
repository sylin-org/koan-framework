using Koan.Canon.Core.Infrastructure;
using Koan.Data.Core.Model;

namespace Koan.Canon.Core.Interceptors;

/// <summary>
/// Fluent builder for registering Canon interceptors with explicit lifecycle timing
/// </summary>
/// <typeparam name="T">The Canon entity type</typeparam>
public class CanonInterceptorBuilder<T> where T : class
{
    private readonly CanonInterceptorRegistry<T> _registry;

    internal CanonInterceptorBuilder(CanonInterceptorRegistry<T> registry)
    {
        _registry = registry;
    }

    #region Before-Stage Interceptors

    /// <summary>
    /// Register an interceptor to execute before intake stage processing
    /// This is where validation, normalization, collision detection, and parking decisions are made
    /// </summary>
    /// <param name="interceptor">The interceptor function to execute</param>
    /// <returns>This builder for fluent chaining</returns>
    public CanonInterceptorBuilder<T> BeforeIntake(Func<T, Task<CanonIntakeAction>> interceptor)
    {
        _registry.RegisterBeforeIntake(interceptor);
        return this;
    }

    /// <summary>
    /// Register an interceptor to execute before keying stage processing
    /// This is where aggregation key preparation and key-based transformations are performed
    /// </summary>
    /// <param name="interceptor">The interceptor function to execute</param>
    /// <returns>This builder for fluent chaining</returns>
    public CanonInterceptorBuilder<T> BeforeKeying(Func<T, Task<CanonStageAction>> interceptor)
    {
        _registry.RegisterBeforeKeying(interceptor);
        return this;
    }

    /// <summary>
    /// Register an interceptor to execute before association stage processing
    /// This is where pre-association conflict detection and entity preparation occurs
    /// </summary>
    /// <param name="interceptor">The interceptor function to execute</param>
    /// <returns>This builder for fluent chaining</returns>
    public CanonInterceptorBuilder<T> BeforeAssociation(Func<T, Task<CanonStageAction>> interceptor)
    {
        _registry.RegisterBeforeAssociation(interceptor);
        return this;
    }

    /// <summary>
    /// Register an interceptor to execute before projection stage processing
    /// This is where Canonical view enrichment and pre-projection transformations occur
    /// </summary>
    /// <param name="interceptor">The interceptor function to execute</param>
    /// <returns>This builder for fluent chaining</returns>
    public CanonInterceptorBuilder<T> BeforeProjection(Func<T, Task<CanonStageAction>> interceptor)
    {
        _registry.RegisterBeforeProjection(interceptor);
        return this;
    }

    /// <summary>
    /// Register an interceptor to execute before materialization stage processing
    /// This is where final data preparation and materialization customization occurs
    /// </summary>
    /// <param name="interceptor">The interceptor function to execute</param>
    /// <returns>This builder for fluent chaining</returns>
    public CanonInterceptorBuilder<T> BeforeMaterialization(Func<T, Task<CanonStageAction>> interceptor)
    {
        _registry.RegisterBeforeMaterialization(interceptor);
        return this;
    }

    #endregion

    #region After-Stage Interceptors

    /// <summary>
    /// Register an interceptor to execute after intake stage processing
    /// This is where post-intake side effects and notifications are triggered
    /// </summary>
    /// <param name="interceptor">The interceptor function to execute</param>
    /// <returns>This builder for fluent chaining</returns>
    public CanonInterceptorBuilder<T> AfterIntake(Func<T, Task<CanonStageAction>> interceptor)
    {
        _registry.RegisterAfterIntake(interceptor);
        return this;
    }

    /// <summary>
    /// Register an interceptor to execute after keying stage processing
    /// This is where post-keying validation and key-based side effects occur
    /// </summary>
    /// <param name="interceptor">The interceptor function to execute</param>
    /// <returns>This builder for fluent chaining</returns>
    public CanonInterceptorBuilder<T> AfterKeying(Func<T, Task<CanonStageAction>> interceptor)
    {
        _registry.RegisterAfterKeying(interceptor);
        return this;
    }

    /// <summary>
    /// Register an interceptor to execute after association stage processing
    /// This is where post-association side effects and notifications are handled
    /// </summary>
    /// <param name="interceptor">The interceptor function to execute</param>
    /// <returns>This builder for fluent chaining</returns>
    public CanonInterceptorBuilder<T> AfterAssociation(Func<T, Task<CanonStageAction>> interceptor)
    {
        _registry.RegisterAfterAssociation(interceptor);
        return this;
    }

    /// <summary>
    /// Register an interceptor to execute after projection stage processing
    /// This is where post-projection cleanup and notifications occur
    /// </summary>
    /// <param name="interceptor">The interceptor function to execute</param>
    /// <returns>This builder for fluent chaining</returns>
    public CanonInterceptorBuilder<T> AfterProjection(Func<T, Task<CanonStageAction>> interceptor)
    {
        _registry.RegisterAfterProjection(interceptor);
        return this;
    }

    /// <summary>
    /// Register an interceptor to execute after materialization stage processing
    /// This is where final cleanup, auditing, and completion notifications occur
    /// </summary>
    /// <param name="interceptor">The interceptor function to execute</param>
    /// <returns>This builder for fluent chaining</returns>
    public CanonInterceptorBuilder<T> AfterMaterialization(Func<T, Task<CanonStageAction>> interceptor)
    {
        _registry.RegisterAfterMaterialization(interceptor);
        return this;
    }

    #endregion

    #region Conditional Interceptors

    /// <summary>
    /// Register an interceptor to execute when association stage succeeds
    /// This is where success-specific side effects and downstream notifications are triggered
    /// </summary>
    /// <param name="interceptor">The interceptor function to execute</param>
    /// <returns>This builder for fluent chaining</returns>
    public CanonInterceptorBuilder<T> OnAssociationSuccess(Func<T, Task<CanonStageAction>> interceptor)
    {
        _registry.RegisterOnAssociationSuccess(interceptor);
        return this;
    }

    /// <summary>
    /// Register an interceptor to execute when association stage fails
    /// This is where failure handling, conflict resolution, and error notifications are managed
    /// </summary>
    /// <param name="interceptor">The interceptor function to execute</param>
    /// <returns>This builder for fluent chaining</returns>
    public CanonInterceptorBuilder<T> OnAssociationFailure(Func<T, Task<CanonStageAction>> interceptor)
    {
        _registry.RegisterOnAssociationFailure(interceptor);
        return this;
    }

    #endregion

    #region Convenience Overloads

    /// <summary>
    /// Register an interceptor to execute before intake stage processing (synchronous convenience overload)
    /// </summary>
    /// <param name="interceptor">The synchronous interceptor function to execute</param>
    /// <returns>This builder for fluent chaining</returns>
    public CanonInterceptorBuilder<T> BeforeIntake(Func<T, CanonIntakeAction> interceptor)
    {
        return BeforeIntake(entity => Task.FromResult(interceptor(entity)));
    }

    /// <summary>
    /// Register an interceptor to execute before any stage processing (synchronous convenience overload)
    /// </summary>
    /// <param name="interceptor">The synchronous interceptor function to execute</param>
    /// <returns>This builder for fluent chaining</returns>
    public CanonInterceptorBuilder<T> BeforeKeying(Func<T, CanonStageAction> interceptor)
    {
        return BeforeKeying(entity => Task.FromResult(interceptor(entity)));
    }

    /// <summary>
    /// Register an interceptor to execute before association stage processing (synchronous convenience overload)
    /// </summary>
    /// <param name="interceptor">The synchronous interceptor function to execute</param>
    /// <returns>This builder for fluent chaining</returns>
    public CanonInterceptorBuilder<T> BeforeAssociation(Func<T, CanonStageAction> interceptor)
    {
        return BeforeAssociation(entity => Task.FromResult(interceptor(entity)));
    }

    /// <summary>
    /// Register an interceptor to execute before projection stage processing (synchronous convenience overload)
    /// </summary>
    /// <param name="interceptor">The synchronous interceptor function to execute</param>
    /// <returns>This builder for fluent chaining</returns>
    public CanonInterceptorBuilder<T> BeforeProjection(Func<T, CanonStageAction> interceptor)
    {
        return BeforeProjection(entity => Task.FromResult(interceptor(entity)));
    }

    /// <summary>
    /// Register an interceptor to execute before materialization stage processing (synchronous convenience overload)
    /// </summary>
    /// <param name="interceptor">The synchronous interceptor function to execute</param>
    /// <returns>This builder for fluent chaining</returns>
    public CanonInterceptorBuilder<T> BeforeMaterialization(Func<T, CanonStageAction> interceptor)
    {
        return BeforeMaterialization(entity => Task.FromResult(interceptor(entity)));
    }

    // Similar overloads for After and Conditional interceptors...
    public CanonInterceptorBuilder<T> AfterIntake(Func<T, CanonStageAction> interceptor)
    {
        return AfterIntake(entity => Task.FromResult(interceptor(entity)));
    }

    public CanonInterceptorBuilder<T> OnAssociationSuccess(Func<T, CanonStageAction> interceptor)
    {
        return OnAssociationSuccess(entity => Task.FromResult(interceptor(entity)));
    }

    public CanonInterceptorBuilder<T> OnAssociationFailure(Func<T, CanonStageAction> interceptor)
    {
        return OnAssociationFailure(entity => Task.FromResult(interceptor(entity)));
    }

    #endregion
}

