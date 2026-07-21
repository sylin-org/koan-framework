using Microsoft.AspNetCore.Http;

namespace Koan.Web.Transformers;

/// <summary>
/// Per-request activation gate for the transformer pipeline. Implementations are expected to be
/// cheap — no I/O, no DB — because they are invoked on every matching response.
/// </summary>
/// <remarks>
/// Three intended consumers:
/// <list type="bullet">
///   <item><description><b>Controllers</b> implement this interface to opt into the transformer
///     pipeline. Returning <c>true</c> unconditionally means "always run transformers"; a stricter
///     controller can gate per-request (for example, off when <c>?raw=1</c> is set). Controllers
///     that do not implement this interface skip the pipeline entirely — there is no longer a
///     separate marker attribute.</description></item>
///   <item><description><b>Enrichers</b> (<see cref="IEntityEnricher{TEntity}"/>) implement this
///     interface to gate per-instance: "I activate only when the user is authenticated", "I
///     activate only for admins", and so on. An enricher without a predicate runs whenever its
///     controller is opted in.</description></item>
///   <item><description><b>Terminal transformers</b> (<see cref="IEntityTransformer{TEntity, TShape}"/>)
///     may implement this interface to add request-context filtering on top of Accept negotiation.</description></item>
/// </list>
/// </remarks>
public interface ITransformerActivationPredicate
{
    /// <summary>
    /// Returns <c>true</c> when this transformer / enricher / controller should participate in the
    /// current request.
    /// </summary>
    bool ShouldActivate(HttpContext context);
}
