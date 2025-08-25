namespace Sora.Web.Transformers;

public sealed record TransformerMatch<TEntity>(string ContentType, object Transformer);