using Koan.Data.Abstractions.Filtering;

namespace Koan.Data.Vector.Abstractions;

/// <summary>
/// Per-adapter anti-corruption contract that renders the unified <see cref="Filter"/> AST into a
/// provider's native metadata-filter representation. The vector analogue of the entity path's
/// <c>IFilterTranslator&lt;TNative&gt;</c> (AI-0036 §9 / DATA-0097 P1).
/// </summary>
/// <remarks>
/// Two deliberate differences from the entity translator:
/// <list type="bullet">
/// <item>It declares <see cref="VectorFilterCapabilities"/> (a single operator set — vector metadata
/// is schemaless, so there is no scalar-vs-collection split).</item>
/// <item><see cref="Translate"/> receives an <b>already-validated, fully-pushable</b> <see cref="Filter"/>
/// (the <c>VectorFilterCoordinator</c> has run the split and rejected any residual as a hard error),
/// so the translator never produces a residual and never falls back to in-memory evaluation. It
/// must throw <see cref="System.NotSupportedException"/> as a defense-in-depth backstop if it ever
/// sees a node/operator outside its declared capabilities.</item>
/// </list>
/// The provider's metadata-key prefix / field-addressing concern (e.g. Milvus/Qdrant nested keys) is
/// carried on the translator instance (via its constructor), so <see cref="Translate"/> stays a
/// uniform <c>Filter -&gt; TNative</c> with no extra parameters.
/// </remarks>
/// <typeparam name="TNative">The provider's native filter representation (e.g. a SQL WHERE fragment, a JObject, a Qdrant filter DTO).</typeparam>
public interface IVectorFilterTranslator<out TNative>
{
    /// <summary>The operators/paths this adapter can faithfully push down onto stored metadata.</summary>
    VectorFilterCapabilities Capabilities { get; }

    /// <summary>
    /// Renders a fully-pushable filter into the provider's native representation. The input is
    /// guaranteed pushable by the coordinator; the translator may assume no residual.
    /// </summary>
    TNative Translate(Filter filter);
}
