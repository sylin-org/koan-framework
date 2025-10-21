using Koan.Data.Abstractions;

namespace Koan.Data.Vector.Abstractions;

public interface IVectorWorkflowRegistry
{
    bool IsEnabled { get; }

    IVectorWorkflow<TEntity> GetWorkflow<TEntity>(string? profileName = null)
        where TEntity : class, IEntity<string>;

    bool IsAvailable<TEntity>(string? profileName = null)
        where TEntity : class, IEntity<string>;
}
