using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sora.Web.Auth.TestProvider.Options;

namespace Sora.Web.Auth.TestProvider.Extensions;

public static class TestProviderEndpointExtensions
{
    public static IEndpointRouteBuilder MapSoraTestProviderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        using var scope = endpoints.ServiceProvider.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<TestProviderOptions>>().Value;
        var prefix = string.IsNullOrWhiteSpace(options.RouteBase) ? "/.testoauth" : options.RouteBase.TrimEnd('/');

        // Map controller actions using conventional routes tied to the configured prefix
        endpoints.MapControllerRoute(
            name: "Sora.TestProvider.Login",
            pattern: $"{prefix}/login.html",
            defaults: new { controller = "Static", action = "LoginPage" });

        endpoints.MapControllerRoute(
            name: "Sora.TestProvider.Authorize",
            pattern: $"{prefix}/authorize",
            defaults: new { controller = "Authorize", action = "Authorize" });

        endpoints.MapControllerRoute(
            name: "Sora.TestProvider.Token",
            pattern: $"{prefix}/token",
            defaults: new { controller = "Token", action = "Token" });

        endpoints.MapControllerRoute(
            name: "Sora.TestProvider.UserInfo",
            pattern: $"{prefix}/userinfo",
            defaults: new { controller = "UserInfo", action = "UserInfo" });

        return endpoints;
    }
}
