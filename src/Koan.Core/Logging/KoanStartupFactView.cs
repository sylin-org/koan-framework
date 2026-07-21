using Koan.Core.Diagnostics;
using Koan.Core.Infrastructure;

namespace Koan.Core.Logging;

/// <summary>One deterministic startup view over the canonical host fact envelope.</summary>
internal sealed record KoanStartupFactView(
    KoanFact[] ModuleFailures,
    KoanFact[] Decisions,
    KoanFact[] Guarantees,
    KoanFact[] Diagnostics)
{
    public static KoanStartupFactView Compile(KoanFactEnvelope? envelope)
    {
        var facts = envelope?.Facts ?? [];
        return new KoanStartupFactView(
            Order(facts.Where(static fact => fact.Code == Constants.Diagnostics.Codes.ModuleRejected)),
            Order(facts.Where(static fact =>
                fact.Kind == KoanFactKind.Election && fact.State == KoanFactState.Selected)),
            Order(facts.Where(static fact =>
                fact.Kind == KoanFactKind.Guarantee
                && fact.State is not (KoanFactState.Degraded or KoanFactState.Rejected or KoanFactState.CollectionFailed))),
            Order(facts.Where(static fact =>
                fact.Code != Constants.Diagnostics.Codes.ModuleRejected
                && fact.State is KoanFactState.Degraded or KoanFactState.Rejected or KoanFactState.CollectionFailed)));
    }

    private static KoanFact[] Order(IEnumerable<KoanFact> facts)
        => facts
            .OrderBy(static fact => fact.Subject, StringComparer.Ordinal)
            .ThenBy(static fact => fact.Code, StringComparer.Ordinal)
            .ToArray();
}
