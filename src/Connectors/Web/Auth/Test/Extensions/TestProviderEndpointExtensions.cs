using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Koan.Web.Auth.Connector.Test.Options;

namespace Koan.Web.Auth.Connector.Test.Extensions;

public static class TestProviderEndpointExtensions
{
    public static IEndpointRouteBuilder MapKoanTestProviderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        using var scope = endpoints.ServiceProvider.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<TestProviderOptions>>().Value;
        var prefix = string.IsNullOrWhiteSpace(options.RouteBase) ? "/.testoauth" : options.RouteBase.TrimEnd('/');

        // Map controller actions using conventional routes tied to the configured prefix
        endpoints.MapControllerRoute(
            name: "Koan.TestProvider.Login",
            pattern: $"{prefix}/login.html",
            defaults: new { controller = "Static", action = "LoginPage" });

        endpoints.MapControllerRoute(
            name: "Koan.TestProvider.Authorize",
            pattern: $"{prefix}/authorize",
            defaults: new { controller = "Authorize", action = "Authorize" });

        endpoints.MapControllerRoute(
            name: "Koan.TestProvider.Token",
            pattern: $"{prefix}/token",
            defaults: new { controller = "Token", action = "Token" });

        endpoints.MapControllerRoute(
            name: "Koan.TestProvider.UserInfo",
            pattern: $"{prefix}/userinfo",
            defaults: new { controller = "UserInfo", action = "UserInfo" });

        return endpoints;
    }
}

