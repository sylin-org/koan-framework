using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Koan.Admin.Infrastructure;
using Koan.Admin.Options;

namespace Koan.Web.Admin.Infrastructure;

internal sealed class KoanAdminDevelopmentPolicySetup(IHostEnvironment environment, IOptionsMonitor<KoanAdminOptions> options)
    : IConfigureOptions<AuthorizationOptions>
{
    public void Configure(AuthorizationOptions authorization)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        var snapshot = options.CurrentValue;
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
