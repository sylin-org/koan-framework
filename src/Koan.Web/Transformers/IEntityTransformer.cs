using Microsoft.AspNetCore.Http;

namespace Koan.Web.Transformers;

public interface IEntityTransformer<TEntity, TShape>
{
    // Accepted content types for output and input for this transformer
    IReadOnlyList<string> AcceptContentTypes { get; }

    // Output transformation
    Task<object> Transform(TEntity model, HttpContext httpContext);
    Task<object> TransformMany(IEnumerable<TEntity> models, HttpContext httpContext);

    // Input transformation (POST/PUT): parse request payload to TEntity or list of TEntity
    Task<TEntity> Parse(Stream body, string contentType, HttpContext httpContext);
    Task<IReadOnlyList<TEntity>> ParseMany(Stream body, string contentType, HttpContext httpContext);
}