namespace Koan.Web.Hosting;

/// <summary>
/// Named positions in Koan's canonical web middleware pipeline (owned by <see cref="KoanWebStartupFilter"/>),
/// where modules may contribute middleware via <see cref="IKoanWebPipelineContributor"/>. Modeled on the
/// OWIN / IIS integrated-pipeline stages. See WEB-0069.
/// </summary>
public enum KoanWebPipelineStage
{
    /// <summary>Before <c>UseRouting()</c> — request context, secure headers, early short-circuits.</summary>
    BeforeRouting,

    /// <summary>After <c>UseAuthentication()</c>, before authorization — dev identity, claims enrichment.</summary>
    AfterAuthentication,

    /// <summary>After <c>UseAuthorization()</c> — post-authorization auditing / tenancy assertions.</summary>
    AfterAuthorization,
}
