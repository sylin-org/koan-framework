using Sora.Data.Abstractions;

namespace Sora.Data.Core.Model;

// Convenience base for the common string key case
public abstract class Data<TEntity> : Data<TEntity, string>
    where TEntity : class, IEntity<string>
{
}
