using Microsoft.AspNetCore.Builder;

namespace Koan.Web.Hosting;

/// <summary>
/// Lets a module contribute middleware into Koan's web pipeline BETWEEN authentication and authorization
/// (e.g. the SEC-0001 §4 zero-config dev identity). <see cref="KoanWebStartupFilter"/> owns the auth-middleware
/// positioning (UseRouting → UseAuthentication → … → UseAuthorization → UseEndpoints); contributors are the
/// supported way for other pillars to inject at that exact point, instead of registering a second startup
/// filter whose ordering relative to <c>KoanWebStartupFilter</c> is not guaranteed (and which can land after
/// the terminal endpoints and never run).
/// </summary>
public interface IPostAuthenticationContributor
{
    void Configure(IApplicationBuilder app);
}
