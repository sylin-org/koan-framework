using System.Collections.Generic;
using Koan.Web.Admin.Infrastructure;
using Microsoft.Extensions.Options;

namespace Koan.Web.Admin.Options;

internal sealed class KoanAdminOptionsValidator : IValidateOptions<KoanAdminOptions>
{
    public ValidateOptionsResult Validate(string? name, KoanAdminOptions options)
    {
        var errors = new List<string>();

        if (!KoanAdminPathUtility.IsValidPrefix(options.PathPrefix))
        {
            errors.Add("Koan:Admin:PathPrefix may only contain letters, digits, '.', '-', or '_' characters.");
        }

        if (options.Authorization is null)
        {
            errors.Add("Koan:Admin:Authorization configuration is required.");
        }
        else if (string.IsNullOrWhiteSpace(options.Authorization.Policy))
        {
            errors.Add("Koan:Admin:Authorization:Policy cannot be blank.");
        }

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }
}
