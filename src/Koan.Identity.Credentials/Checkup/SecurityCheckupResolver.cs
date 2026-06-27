namespace Koan.Identity.Credentials.Checkup;

/// <summary>
/// SEC-0007 P3-grp4 — flattens every discovered <see cref="ISecurityCheckupContributor"/> into one
/// <see cref="SecurityCheckup"/> for a person. The overall traffic-light is the <b>worst</b> signal, so a single Red
/// dominates the landing and a person at all-Green sees a calm "you're secure". No new storage — pure projection.
/// </summary>
public sealed class SecurityCheckupResolver
{
    private readonly IEnumerable<ISecurityCheckupContributor> _contributors;

    public SecurityCheckupResolver(IEnumerable<ISecurityCheckupContributor> contributors) => _contributors = contributors;

    public async Task<SecurityCheckup> EvaluateAsync(string identityId, CancellationToken ct = default)
    {
        var signals = new List<CheckupSignal>();
        foreach (var c in _contributors)
            signals.AddRange(await c.EvaluateAsync(identityId, ct).ConfigureAwait(false));

        var overall = signals.Count == 0 ? CheckupGrade.Green : signals.Max(s => s.Grade);
        return new SecurityCheckup(identityId, overall, signals);
    }
}
