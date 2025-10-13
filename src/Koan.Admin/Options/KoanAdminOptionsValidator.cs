using System.Collections.Generic;
using Koan.Admin.Infrastructure;
using Microsoft.Extensions.Options;

namespace Koan.Admin.Options;

internal sealed class KoanAdminOptionsValidator : IValidateOptions<KoanAdminOptions>
{
    public ValidateOptionsResult Validate(string? name, KoanAdminOptions options)
    {
        var errors = new List<string>();

        if (!KoanAdminPathUtility.IsValidPrefix(options.PathPrefix))
        {
            errors.Add("Koan:Admin:PathPrefix may only contain letters, digits, '.', '-', or '_' characters.");
        }

        var normalized = KoanAdminPathUtility.NormalizePrefix(options.PathPrefix);
        if (normalized.StartsWith(".", StringComparison.Ordinal) &&
            (Koan.Core.KoanEnv.IsProduction || Koan.Core.KoanEnv.IsStaging) &&
            !options.AllowDotPrefixInProduction)
        {
            errors.Add("Dot-prefixed admin path is not allowed outside Development unless AllowDotPrefixInProduction is true.");
        }

        if ((Koan.Core.KoanEnv.IsProduction || Koan.Core.KoanEnv.IsStaging) &&
            options.Enabled &&
            !options.AllowInProduction)
        {
            errors.Add("Koan Admin cannot be enabled in Production/Staging without AllowInProduction=true.");
        }

        if (options.Enabled && !options.EnableConsoleUi && !options.EnableWeb)
        {
            errors.Add("At least one Koan Admin surface (Console or Web) must be enabled when Koan:Admin:Enabled is true.");
        }

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }
}
