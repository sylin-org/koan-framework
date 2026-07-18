using Microsoft.Extensions.Hosting;

namespace Koan.Web.Auth.Contributors;

/// <summary>
/// Context passed to <see cref="Flow.IKoanAuthFlowHandler.OnBootstrap"/>. Provides the host
/// service provider and environment for one-time set-wide reconciliation work at startup.
/// </summary>
/// <remarks>
/// Contributors that need to iterate users (e.g. backfill role state, seed admin, reconcile
/// against an external source) should use Koan <c>Entity&lt;T&gt;</c> statics directly — for
/// example, <c>await User.All(ct)</c>. The framework deliberately does not expose a generic
/// "user set" abstraction; each contributor owns the entity it operates on.
/// </remarks>
public sealed record AuthBootstrapContext(IServiceProvider Services, IHostEnvironment Environment);
