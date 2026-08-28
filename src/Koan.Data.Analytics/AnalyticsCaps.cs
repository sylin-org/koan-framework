using Koan.Core.Capabilities;

namespace Koan.Data.Analytics;

/// <summary>
/// The analytics pillar's capability tokens (ARCH-0084 pattern: tokens live beside the module that owns
/// them, so referencing it is what surfaces them). Each token names an adapter or engine <b>guarantee</b>
/// and is co-defined with the conformance check that proves it — an engine that declares a token without
/// realizing it fails the matching spec.
/// </summary>
public static class AnalyticsCaps
{
    /// <summary>
    /// The connector can serve as the analytics execution substrate. Declared by
    /// <c>Sylin.Koan.Data.Connector.DuckDb</c> (the reference engine). The pillar refuses to compose
    /// without exactly this capability somewhere in the host.
    /// </summary>
    public static readonly Capability Engine = new("analytics.engine");

    /// <summary>Named, parameter-free questions can be declared and executed on demand.</summary>
    public static readonly Capability QuestionRun = new("analytics.question.run");

    /// <summary>The declared questions form a self-describing, machine-consumable catalog.</summary>
    public static readonly Capability Catalog = new("analytics.catalog");

    /// <summary>Answer provenance (recipe, engine, age, bounds) travels on every result.</summary>
    public static readonly Capability Provenance = new("analytics.provenance");

    /// <summary>
    /// Recipes materialize into the engine and serve within a declared freshness tolerance. Reserved for
    /// ANL-3 — no v0 connector may declare it.
    /// </summary>
    public static readonly Capability Projection = new("analytics.projection");
}
