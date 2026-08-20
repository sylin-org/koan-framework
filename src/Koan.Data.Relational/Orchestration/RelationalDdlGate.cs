using Koan.Core;

namespace Koan.Data.Relational.Orchestration;

/// <summary>
/// Automatic schema creation, described once for every relational adapter.
///
/// <para>Three adapters used to spell this gate three ways, and none of them honored
/// <c>Koan:AllowMagicInProduction</c> even though DATA-0046 says auto-DDL is exactly what that flag governs.
/// The law now lives in <see cref="KoanEnv.Gate"/>; this type only supplies the words, so a Postgres refusal
/// and a SQLite refusal say the same thing about the same risk.</para>
///
/// <para>Callers keep their own message. The gate decides <i>whether</i>, the adapter says <i>which table</i> —
/// a refusal that cannot name the object it refused is not much of a refusal.</para>
/// </summary>
public static class RelationalDdlGate
{
    /// <summary>Auto-DDL as a production risk. <paramref name="consent"/> is the source's <c>AllowProductionDdl</c>.</summary>
    public static KoanMagic Magic(bool consent) => new(
        Capability: "automatic schema creation",
        Risk: "Koan issues CREATE and ALTER against whatever database the connection string resolves to, "
            + "which in production is live data.",
        Remedy: "provision the schema out of band and set DdlPolicy to Validate, or set AllowProductionDdl "
            + "on the source to accept automatic DDL there",
        Consent: consent);

    /// <summary>Whether auto-DDL may run here, given the source's own <c>AllowProductionDdl</c> setting.</summary>
    public static bool Allowed(bool consent) => KoanEnv.Gate.Allows(Magic(consent));

    /// <summary>
    /// The environment half of a refusal, for adapters composing a message that also names the table. Kept
    /// identical across adapters so operators recognize it wherever they meet it.
    /// </summary>
    public const string Refusal =
        "Automatic DDL is not allowed in Production. Provision the schema out of band, set AllowProductionDdl "
        + "on the source, or set Koan:AllowMagicInProduction=true to accept automatic DDL framework-wide.";
}
