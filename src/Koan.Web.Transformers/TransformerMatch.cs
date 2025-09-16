namespace Koan.Web.Transformers;

public sealed record TransformerMatch<TEntity>(string ContentType, object Transformer);