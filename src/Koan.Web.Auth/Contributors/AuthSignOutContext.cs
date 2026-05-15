using Microsoft.AspNetCore.Http;

namespace Koan.Web.Auth.Contributors;

/// <summary>
/// Context passed to <see cref="IKoanAuthEventContributor.OnSignOut"/>. The user id may be
/// <see langword="null"/> when the outgoing cookie did not carry a resolvable platform identity
/// (e.g. an interrupted sign-in that produced a partial cookie).
/// </summary>
public sealed record AuthSignOutContext(string? UserId, IServiceProvider Services, HttpContext HttpContext);
