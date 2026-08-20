using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Core;

/// <summary>
/// Where an automatic convenience stands in the current environment. Produced by
/// <see cref="KoanEnv.Gate.Evaluate"/>; every value except <see cref="Refused"/> means the convenience runs.
/// </summary>
public enum MagicVerdict
{
    /// <summary>Development. The convenience runs silently — this is the ordinary inner loop.</summary>
    Allowed,

    /// <summary>
    /// Neither Development nor Production (Staging, Test, CI, or unset). The convenience still runs, because
    /// refusing here breaks CI and staging for no safety gain, but it announces itself so nobody discovers the
    /// posture by accident later.
    /// </summary>
    AllowedWithNotice,

    /// <summary>Production, with explicit consent. The convenience runs and says so at warning level.</summary>
    AllowedByConsent,

    /// <summary>Production without consent. The convenience refuses.</summary>
    Refused
}

/// <summary>
/// One automatic convenience, described well enough that Koan can explain itself when it refuses.
///
/// <para>Every field exists because a refusal without it is useless. <paramref name="Capability"/> names what
/// stopped, <paramref name="Risk"/> says why Production is different, and <paramref name="Remedy"/> tells the
/// operator what to do instead. Passing vague strings produces a vague error at 3am.</para>
/// </summary>
/// <param name="Capability">
/// What the convenience does, as a noun phrase that completes "Koan refuses ___ in Production" —
/// e.g. <c>"relational DDL"</c>, <c>"a local-custody key"</c>, <c>"AI provider auto-discovery"</c>.
/// </param>
/// <param name="Risk">
/// One sentence on what could go wrong in Production. This is the half an operator actually needs.
/// </param>
/// <param name="Remedy">
/// The capability-specific way to proceed deliberately — the option to set, or the service to register.
/// The framework-wide escape hatch is appended automatically; do not restate it here.
/// </param>
/// <param name="Consent">
/// The capability's own opt-in (<c>AllowProductionDdl</c>, <c>AllowDiscoveryInNonDev</c>, …). True means the
/// application asked for this specific behavior in Production, which is stronger evidence of intent than the
/// framework-wide flag and is why capabilities keep their own switch.
/// </param>
public readonly record struct KoanMagic(string Capability, string Risk, string Remedy, bool Consent = false);

public static partial class KoanEnv
{
    /// <summary>
    /// The single place Koan decides whether a behavior may run in the current environment.
    ///
    /// <para><b>Why this exists.</b> <see cref="KoanEnv.IsDevelopment"/> and <see cref="KoanEnv.IsProduction"/>
    /// are honest facts, and reading them is fine for diagnostics — log verbosity, banners, how much detail a
    /// health payload carries. They are the wrong tool for deciding whether a <i>capability</i> composes,
    /// because that decision has a law, and re-deriving the law at each call site is how call sites drift
    /// apart. Three real drifts came from exactly that: auto-DDL gated on <c>!IsProduction</c> while AI
    /// discovery gated on <c>IsDevelopment</c> (so Staging silently behaved differently for no stated reason),
    /// and neither honored <c>Koan:AllowMagicInProduction</c> even though
    /// <see href="https://github.com/sylin-org/koan-framework/blob/main/docs/decisions/DATA-0046-sqlite-schema-governance-ddl-policy.md">DATA-0046</see>
    /// says it should.</para>
    ///
    /// <para><b>Choosing between the two gates.</b> They are deliberately not the same call, because they are
    /// not the same decision and one of them must never be unlockable:</para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <see cref="KoanMagic"/> — a convenience that is <i>safe everywhere but risky in Production</i>: schema
    ///     auto-creation, endpoint auto-discovery, a local key file. It runs by default, and in Production it
    ///     asks for consent rather than disappearing. Reach for this when the honest sentence is
    ///     "this is fine until it is production data".
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="DevelopmentOnly"/> — a surface that <i>must not exist</i> outside Development: the admin
    ///     UI, the dev token endpoint, seeded credentials, the test auth provider. No flag unlocks it, on
    ///     purpose. Reach for this when the honest sentence is "shipping this would be a vulnerability".
    ///   </description></item>
    /// </list>
    ///
    /// <para><b>The law a <see cref="KoanMagic"/> gate applies.</b> Production is the gate, not Development. A capability
    /// works from a bare reference in every environment and asks for explicit consent only in Production —
    /// consent being either the capability's own option or the framework-wide
    /// <c>Koan:AllowMagicInProduction</c>. Gating on <c>!IsDevelopment()</c> instead turns a production safety
    /// rail into a functionality block that breaks Test, Staging, and CI.</para>
    ///
    /// <example>
    /// The usual shape — refuse in Production, warn outside Development, silent in Development:
    /// <code>
    /// KoanEnv.Gate.Enforce(new KoanMagic(
    ///     Capability: "relational DDL",
    ///     Risk: "schema changes are applied directly to whatever database the connection string points at",
    ///     Remedy: "provision the schema out of band, or set AllowProductionDdl on the source",
    ///     Consent: policy.AllowProductionDdl), environment, logger);
    /// </code>
    /// When refusing would be wrong and skipping is correct, use <see cref="Announce"/>, which never throws.
    /// </example>
    /// </summary>
    public static class Gate
    {
        /// <summary>
        /// Decide where <paramref name="magic"/> stands, without acting on it. Prefer <see cref="Enforce"/> or
        /// <see cref="Announce"/>, which also produce the explanation; reach for this when the verdict feeds a
        /// larger policy decision the caller reports itself.
        /// </summary>
        /// <param name="magic">The convenience being considered, and the capability's own consent flag.</param>
        /// <param name="environment">
        /// The host environment, when the caller has one injected. Passing it keeps the decision testable and
        /// scoped to this host. Omit it only before DI exists, where the static snapshot is the only source.
        /// </param>
        public static MagicVerdict Evaluate(in KoanMagic magic, IHostEnvironment? environment = null)
        {
            var production = environment?.IsProduction() ?? IsProduction;
            if (!production)
            {
                var development = environment?.IsDevelopment() ?? IsDevelopment;
                return development ? MagicVerdict.Allowed : MagicVerdict.AllowedWithNotice;
            }

            // The framework-wide flag is a second key, not a replacement for the capability's own. Either is
            // consent; requiring both would make the global flag useless, and requiring neither would make
            // Production indistinguishable from Development.
            return magic.Consent || AllowMagicInProduction
                ? MagicVerdict.AllowedByConsent
                : MagicVerdict.Refused;
        }

        /// <summary>Whether <paramref name="magic"/> may run here. The bool form of <see cref="Evaluate"/>.</summary>
        public static bool Allows(in KoanMagic magic, IHostEnvironment? environment = null)
            => Evaluate(magic, environment) != MagicVerdict.Refused;

        /// <summary>
        /// Run the convenience or refuse the host. Throws <see cref="InvalidOperationException"/> in Production
        /// without consent, warns when the posture deserves saying out loud, and stays silent in Development.
        ///
        /// <para>Use this when proceeding without the convenience would leave the application broken or
        /// silently wrong — an entity with no table, a key nobody can decrypt with. Refusing at boot beats
        /// failing on the first request.</para>
        /// </summary>
        /// <exception cref="InvalidOperationException">Production, and neither consent was given.</exception>
        public static void Enforce(in KoanMagic magic, IHostEnvironment? environment = null, ILogger? logger = null)
        {
            var verdict = Evaluate(magic, environment);
            if (verdict == MagicVerdict.Refused) throw new InvalidOperationException(Refusal(magic));
            Notify(magic, verdict, environment, logger);
        }

        /// <summary>
        /// Report whether the convenience may run, and say so when the posture deserves it. Never throws.
        ///
        /// <para>Use this when skipping is a coherent outcome — discovery that finds nothing, a probe that
        /// stays off. A refusal here is not an error, so it is logged at information level: the operator asked
        /// for Production, and Production behaving like Production is the expected result.</para>
        /// </summary>
        /// <returns><see langword="true"/> when the caller should proceed.</returns>
        public static bool Announce(in KoanMagic magic, ILogger? logger = null, IHostEnvironment? environment = null)
        {
            var verdict = Evaluate(magic, environment);
            if (verdict == MagicVerdict.Refused)
            {
                logger?.LogInformation(
                    "Koan skipped {Capability} in Production. {Risk} To enable it: {Remedy}, or set {Flag}=true.",
                    magic.Capability, magic.Risk, magic.Remedy, Infrastructure.Constants.Configuration.Koan.AllowMagicInProduction);
                return false;
            }

            Notify(magic, verdict, environment, logger);
            return true;
        }

        /// <summary>
        /// Whether development-only surfaces may exist in this host.
        ///
        /// <para><b>No flag unlocks this, and that is the point.</b> It is deliberately decoupled from
        /// <c>Koan:AllowMagicInProduction</c> per
        /// <see href="https://github.com/sylin-org/koan-framework/blob/main/docs/decisions/SEC-0001-fleet-identity-and-trust-fabric.md">SEC-0001</see>:
        /// a convenience flag that also switched on the admin UI, seeded credentials, or the dev token endpoint
        /// would turn one careless environment variable into an authentication bypass. Whatever this gate
        /// guards should be absent from the production DI graph entirely, not merely unreachable.</para>
        ///
        /// <para>If you find yourself wanting an override, the surface belongs behind a <see cref="KoanMagic"/> gate
        /// instead — which is a real design question worth answering deliberately, not a call-site workaround.</para>
        /// </summary>
        /// <param name="environment">The injected host environment; omit only before DI exists.</param>
        public static bool DevelopmentOnly(IHostEnvironment? environment = null)
            => environment?.IsDevelopment() ?? IsDevelopment;

        /// <summary>
        /// Whether this host looks like a real deployment rather than someone's machine — Production, or
        /// anything running in a container.
        ///
        /// <para>Use it to pick a <i>default</i> for a setting the application can still override: require
        /// authentication unless told otherwise, leave an explorer UI off unless asked for. A developer running
        /// in Docker Compose gets the cautious default, which is the right way round — the cost is one explicit
        /// setting, and the alternative is an unauthenticated endpoint nobody meant to publish.</para>
        ///
        /// <para><b>This is a heuristic, not a security boundary.</b> Container detection is evidence, not
        /// proof, so never let it decide whether a request is authorized — only what the unconfigured default
        /// should be. Anything that must hold under attack belongs behind <see cref="DevelopmentOnly"/> or an
        /// explicit policy.</para>
        /// </summary>
        /// <param name="environment">The injected host environment; omit only before DI exists.</param>
        public static bool LooksDeployed(IHostEnvironment? environment = null)
            => (environment?.IsProduction() ?? IsProduction) || InContainer;

        private static string Refusal(in KoanMagic magic)
            => $"Koan refuses {magic.Capability} in Production. {magic.Risk} " +
               $"To allow it: {magic.Remedy}, or set {Infrastructure.Constants.Configuration.Koan.AllowMagicInProduction}=true " +
               $"to accept this class of risk framework-wide.";

        private static void Notify(in KoanMagic magic, MagicVerdict verdict, IHostEnvironment? environment, ILogger? logger)
        {
            if (verdict == MagicVerdict.Allowed || logger is null) return;

            var environmentName = environment?.EnvironmentName ?? EnvironmentName;
            if (verdict == MagicVerdict.AllowedByConsent)
            {
                logger.LogWarning(
                    "Koan is running {Capability} in Production by explicit consent. {Risk}",
                    magic.Capability, magic.Risk);
                return;
            }

            logger.LogWarning(
                "Koan is running {Capability} in environment '{Environment}'. {Risk} " +
                "This is allowed outside Production; {Remedy} before this reaches it.",
                magic.Capability, environmentName, magic.Risk, magic.Remedy);
        }
    }
}
