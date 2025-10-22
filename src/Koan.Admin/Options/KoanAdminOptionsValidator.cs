using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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

        if (options.Enabled && !options.EnableConsoleUi && !options.EnableWeb && !options.EnableLaunchKit)
        {
            errors.Add("At least one Koan Admin surface (Console, Web, or LaunchKit) must be enabled when Koan:Admin:Enabled is true.");
        }

        if (options.EnableLaunchKit && !options.EnableWeb)
        {
            errors.Add("Koan Admin LaunchKit requires Koan:Admin:EnableWeb=true because it is delivered via the web surface.");
        }

        if (options.Generate is null)
        {
            errors.Add("Koan:Admin:Generate configuration is required.");
        }
        else
        {
            ValidateGenerate(options.Generate, errors);
        }

        return errors.Count > 0 ? ValidateOptionsResult.Fail(errors) : ValidateOptionsResult.Success;
    }

    private static void ValidateGenerate(KoanAdminGenerateOptions generate, List<string> errors)
    {
        if (generate.ComposeProfiles is null || generate.ComposeProfiles.Length == 0)
        {
            errors.Add("Koan:Admin:Generate:ComposeProfiles must include at least one profile name.");
        }
        else
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in generate.ComposeProfiles)
            {
                if (string.IsNullOrWhiteSpace(profile))
                {
                    errors.Add("Koan:Admin:Generate:ComposeProfiles entries cannot be blank.");
                    continue;
                }

                if (!ProfilePattern.IsMatch(profile))
                {
                    errors.Add($"Compose profile '{profile}' may only contain letters, digits, '-', or '_' characters.");
                }

                if (!seen.Add(profile))
                {
                    errors.Add($"Compose profile '{profile}' is duplicated. Profiles must be unique (case-insensitive).");
                }
            }
        }

        if (generate.OpenApiClients is not null)
        {
            foreach (var client in generate.OpenApiClients)
            {
                if (string.IsNullOrWhiteSpace(client))
                {
                    errors.Add("Koan:Admin:Generate:OpenApiClients entries cannot be blank.");
                }
            }
        }

        if (generate.ComposeBasePort < 0)
        {
            errors.Add("Koan:Admin:Generate:ComposeBasePort must be zero or positive.");
        }
    }

    private static readonly Regex ProfilePattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
