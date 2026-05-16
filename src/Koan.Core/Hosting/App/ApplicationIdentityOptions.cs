using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Core.Hosting.App;

public sealed class ApplicationIdentityOptions
{
    public string? Name { get; set; }
    public string? Code { get; set; }
    public string? Description { get; set; }
    public string? ContactEmail { get; set; }
    public string? SupportUrl { get; set; }
    public string[] Tags { get; set; } = [];

    internal ApplicationIdentitySnapshot ToSnapshot()
    {
        var tags = Tags?.Where(static t => !string.IsNullOrWhiteSpace(t))
            .Select(static t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        return new ApplicationIdentitySnapshot(
            string.IsNullOrWhiteSpace(Name) ? "Koan Application" : Name.Trim(),
            string.IsNullOrWhiteSpace(Code) ? "koan-app" : Code.Trim(),
            Description?.Trim() ?? "",
            string.IsNullOrWhiteSpace(ContactEmail) ? null : ContactEmail!.Trim(),
            string.IsNullOrWhiteSpace(SupportUrl) ? null : SupportUrl!.Trim(),
            tags);
    }
}

public readonly record struct ApplicationIdentitySnapshot(
    string Name,
    string Code,
    string Description,
    string? ContactEmail,
    string? SupportUrl,
    IReadOnlyList<string> Tags)
{
    public static readonly ApplicationIdentitySnapshot Empty = new(
        "Koan Application",
        "koan-app",
        "",
        null,
        null,
        []);
}

internal static class ApplicationIdentityDefaults
{
    public const string ConfigurationSection = Infrastructure.ConfigurationConstants.Application.Section;

    public static ApplicationIdentitySnapshot Resolve(IConfiguration? cfg, IHostEnvironment? env)
    {
        var options = new ApplicationIdentityOptions();
        cfg?.GetSection(ConfigurationSection).Bind(options);
        Apply(options, cfg, env);
        return options.ToSnapshot();
    }

    public static void Apply(ApplicationIdentityOptions options, IConfiguration? cfg, IHostEnvironment? env)
    {
        if (options is null)
        {
            return;
        }

        options.Tags ??= [];

        var assembly = ResolveAssembly(env);
        var koanApp = assembly?.GetCustomAttribute<KoanAppAttribute>();
        var assemblyName = assembly?.GetName()?.Name;
        var title = assembly?.GetCustomAttribute<AssemblyTitleAttribute>()?.Title;
        var product = assembly?.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
        var description = assembly?.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description;

        options.Name = Coalesce(options.Name, koanApp?.Name, title, product, env?.ApplicationName, assemblyName, "Koan Application");
        options.Code = NormalizeCode(options.Code, options.Name, koanApp?.Code);
        options.Description = Coalesce(options.Description, koanApp?.Description, description, "");

        if (string.IsNullOrWhiteSpace(options.ContactEmail))
        {
            var email = Coalesce(koanApp?.ContactEmail, Configuration.Read<string?>(cfg, Infrastructure.ConfigurationConstants.Application.ContactEmail, null));
            options.ContactEmail = string.IsNullOrWhiteSpace(email) ? options.ContactEmail : email;
        }

        if (string.IsNullOrWhiteSpace(options.SupportUrl))
        {
            var url = Coalesce(koanApp?.SupportUrl, Configuration.Read<string?>(cfg, Infrastructure.ConfigurationConstants.Application.SupportUrl, null));
            options.SupportUrl = string.IsNullOrWhiteSpace(url) ? options.SupportUrl : url;
        }

        if (koanApp is not null && koanApp.Tags.Length > 0)
        {
            var existing = options.Tags ?? [];
            options.Tags = existing
                .Concat(koanApp.Tags)
                .Where(static t => !string.IsNullOrWhiteSpace(t))
                .Select(static t => t.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    private static Assembly? ResolveAssembly(IHostEnvironment? env)
    {
        var assembly = Assembly.GetEntryAssembly();
        if (assembly is not null)
        {
            return assembly;
        }

        var appName = env?.ApplicationName;
        if (!string.IsNullOrWhiteSpace(appName))
        {
            assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, appName, StringComparison.OrdinalIgnoreCase));
            if (assembly is not null)
            {
                return assembly;
            }
        }

        return typeof(ApplicationIdentityDefaults).Assembly;
    }

    private static string Coalesce(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value!.Trim();
            }
        }

        return "";
    }

    private static string NormalizeCode(string? existingCode, string? resolvedName, string? attributeCode)
    {
        if (!string.IsNullOrWhiteSpace(attributeCode))
        {
            return Slugify(attributeCode);
        }

        if (!string.IsNullOrWhiteSpace(resolvedName))
        {
            return Slugify(resolvedName);
        }

        return "koan-app";
    }

    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "koan-app";
        }

        var sb = new StringBuilder(value.Length);
        var lastWasDash = false;

        foreach (var ch in value.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                lastWasDash = false;
            }
            else
            {
                if (!lastWasDash)
                {
                    sb.Append('-');
                    lastWasDash = true;
                }
            }
        }

        var result = sb.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "koan-app" : result;
    }
}

internal sealed class ApplicationIdentityPostConfigure : IPostConfigureOptions<ApplicationIdentityOptions>
{
    private readonly IConfiguration? _cfg;
    private readonly IHostEnvironment? _env;

    public ApplicationIdentityPostConfigure(IConfiguration? cfg, IHostEnvironment? env)
    {
        _cfg = cfg;
        _env = env;
    }

    public void PostConfigure(string? name, ApplicationIdentityOptions options)
    {
        ApplicationIdentityDefaults.Apply(options, _cfg, _env);
    }
}
