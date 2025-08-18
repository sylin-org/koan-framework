using Microsoft.AspNetCore.Http;

namespace Sora.Web.Transformers;

public interface IEntityTransformer<TEntity, TShape>
{
    // Accepted content types for output and input for this transformer
    IReadOnlyList<string> AcceptContentTypes { get; }

    // Output transformation
    Task<object> TransformAsync(TEntity model, HttpContext httpContext);
    Task<object> TransformManyAsync(IEnumerable<TEntity> models, HttpContext httpContext);

    // Input transformation (POST/PUT): parse request payload to TEntity or list of TEntity
    Task<TEntity> ParseAsync(Stream body, string contentType, HttpContext httpContext);
    Task<IReadOnlyList<TEntity>> ParseManyAsync(Stream body, string contentType, HttpContext httpContext);
}

public interface ITransformerRegistry
{
    void Register<TEntity, TShape>(IEntityTransformer<TEntity, TShape> transformer, string[] contentTypes, int priority = (int)TransformerPriority.Discovered);
    TransformerMatch<TEntity>? ResolveForOutput<TEntity>(IEnumerable<string> acceptTypes);
    TransformerMatch<TEntity>? ResolveForInput<TEntity>(string contentType);
    IReadOnlyList<string> GetContentTypes<TEntity>();
}

public sealed record TransformerMatch<TEntity>(string ContentType, object Transformer);

public enum TransformerPriority
{
    Discovered = 0,
    Explicit = 10
}
