namespace Koan.Web.Transformers;

public interface ITransformerRegistry
{
    void Register<TEntity, TShape>(IEntityTransformer<TEntity, TShape> transformer, string[] contentTypes, int priority = (int)TransformerPriority.Discovered);
    TransformerMatch<TEntity>? ResolveForOutput<TEntity>(IEnumerable<string> acceptTypes);
    TransformerMatch<TEntity>? ResolveForInput<TEntity>(string contentType);
    IReadOnlyList<string> GetContentTypes<TEntity>();
}