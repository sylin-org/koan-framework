using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Web.Admin.Options;

namespace Koan.Web.Admin.Infrastructure;

internal sealed class KoanAdminDevelopmentPolicySetup(IHostEnvironment environment, IOptions<KoanAdminOptions> options)
    : IConfigureOptions<AuthorizationOptions>
{
    public void Configure(AuthorizationOptions authorization)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        var snapshot = options.Value;
        var policyName = snapshot.Authorization.Policy ?? KoanAdminDefaults.Policy;

        if (!snapshot.Authorization.AutoCreateDevelopmentPolicy)
        {
            return;
        }

        if (authorization.GetPolicy(policyName) is not null)
        {
            return;
        }

        authorization.AddPolicy(policyName, builder =>
        {
            builder.RequireAuthenticatedUser();
        });
    }
}
