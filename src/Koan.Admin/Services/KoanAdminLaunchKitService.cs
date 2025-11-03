using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using Koan.Admin.Contracts;
using Koan.Admin.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Admin.Services;

internal sealed class KoanAdminLaunchKitService : IKoanAdminLaunchKitService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IKoanAdminManifestService _manifestService;
    private readonly IOptionsMonitor<KoanAdminOptions> _options;
    private readonly IHostEnvironment _environment;
    private readonly IKoanAdminRouteProvider _routes;
    private readonly ILogger<KoanAdminLaunchKitService> _logger;

    public KoanAdminLaunchKitService(
        IKoanAdminManifestService manifestService,
        IOptionsMonitor<KoanAdminOptions> options,
        IHostEnvironment environment,
        IKoanAdminRouteProvider routes,
        ILogger<KoanAdminLaunchKitService> logger)
    {
        _manifestService = manifestService;
        _options = options;
        _environment = environment;
        _routes = routes;
        _logger = logger;
    }

    public Task<KoanAdminLaunchKitMetadata> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        var generate = _options.CurrentValue.Generate ?? new KoanAdminGenerateOptions();
        var profiles = NormalizeProfiles(generate.ComposeProfiles);
        var defaultProfile = profiles.Length > 0 ? profiles[0] : "Default";
        var openApiTemplates = generate.OpenApiClients?.Where(s => !string.IsNullOrWhiteSpace(s)).Select(NormalizeClient).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? Array.Empty<string>();

        var metadata = new KoanAdminLaunchKitMetadata(
            defaultProfile,
            profiles,
            openApiTemplates,
            generate.IncludeAppSettings,
            generate.IncludeCompose,
            generate.IncludeAspire,
            generate.IncludeManifest,
            generate.IncludeReadme
        );

        _logger.LogDebug("LaunchKit metadata requested. Profiles: {Profiles}; OpenAPI: {Clients}", profiles, openApiTemplates);

        return Task.FromResult(metadata);
    }

    public async Task<KoanAdminLaunchKitArchive> GenerateArchiveAsync(
        KoanAdminLaunchKitRequest request,
        CancellationToken cancellationToken = default)
    {
        request ??= new KoanAdminLaunchKitRequest(null, null, null, null, null, null, null);
        var options = _options.CurrentValue;
        var generate = options.Generate ?? new KoanAdminGenerateOptions();
        var profiles = NormalizeProfiles(generate.ComposeProfiles);
        var profile = ResolveProfile(request.Profile, profiles);

        var includeAppSettings = request.IncludeAppSettings ?? generate.IncludeAppSettings;
        var includeCompose = request.IncludeCompose ?? generate.IncludeCompose;
        var includeAspire = request.IncludeAspire ?? generate.IncludeAspire;
        var includeManifest = request.IncludeManifest ?? generate.IncludeManifest;
        var includeReadme = request.IncludeReadme ?? generate.IncludeReadme;
        var openApiClients = ResolveOpenApiClients(request.OpenApiClients, generate.OpenApiClients);

        var generatedAt = DateTimeOffset.UtcNow;
        var manifest = await _manifestService.BuildAsync(cancellationToken);
        var routes = _routes.Current;

        var files = new List<FileBuffer>();

        if (includeAppSettings)
        {
            var content = BuildAppSettings(manifest, profile, generatedAt);
            files.Add(new FileBuffer($"config/appsettings.{profile.ToLowerInvariant()}.json", "application/json", content));
        }

        if (includeCompose)
        {
            var content = BuildCompose(manifest, profile, generate.ComposeBasePort);
            files.Add(new FileBuffer($"compose/docker-compose.{profile.ToLowerInvariant()}.yml", "application/x-yaml", content));
        }

        if (includeAspire)
        {
            var content = BuildAspire(manifest, profile);
            files.Add(new FileBuffer($"aspire/aspire.apphost.{profile.ToLowerInvariant()}.json", "application/json", content));
        }

        if (includeManifest)
        {
            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            files.Add(new FileBuffer("manifest/koan-manifest.json", "application/json", Encoding.UTF8.GetBytes(manifestJson)));
        }

        if (includeReadme)
        {
            var readme = BuildReadme(manifest, profile, generatedAt, routes);
            files.Add(new FileBuffer("README.md", "text/markdown", Encoding.UTF8.GetBytes(readme)));
        }

        var metadata = BuildMetadata(manifest, profile, generatedAt, routes, includeAppSettings, includeCompose, includeAspire, includeManifest, openApiClients);
        files.Add(new FileBuffer("metadata/launchkit.json", "application/json", metadata));

        foreach (var client in openApiClients)
        {
            var content = BuildOpenApiInstructions(client, profile, routes);
            files.Add(new FileBuffer($"openapi/{client}/README.md", "text/markdown", Encoding.UTF8.GetBytes(content)));
        }

        var bundle = CreateBundle(profile, generatedAt, files);
        var archiveContent = CreateArchive(files);
        var archiveName = $"koan-launchkit-{profile.ToLowerInvariant()}-{generatedAt:yyyyMMddHHmmss}.zip";

        _logger.LogInformation("Generated LaunchKit bundle {Archive} with {FileCount} files for profile {Profile}", archiveName, files.Count, profile);

        return new KoanAdminLaunchKitArchive(archiveName, "application/zip", bundle, archiveContent);
    }

    private static KoanAdminLaunchKitBundle CreateBundle(string profile, DateTimeOffset generatedAt, IReadOnlyList<FileBuffer> files)
    {
        var bundleFiles = files
            .Select(f => new KoanAdminLaunchKitFile(f.Path, f.ContentType, f.Content.Length))
            .ToList();
        return new KoanAdminLaunchKitBundle(profile, generatedAt, bundleFiles);
    }

    private static byte[] CreateArchive(IReadOnlyList<FileBuffer> files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Path, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                entryStream.Write(file.Content, 0, file.Content.Length);
            }
        }

        return stream.ToArray();
    }

    private static string[] NormalizeProfiles(string[]? profiles)
    {
        if (profiles is null || profiles.Length == 0)
        {
            return new[] { "Default" };
        }

        return profiles
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveProfile(string? requested, string[] profiles)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var match = profiles.FirstOrDefault(p => string.Equals(p, requested, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return profiles.Length > 0 ? profiles[0] : "Default";
    }

    private static string[] ResolveOpenApiClients(IReadOnlyList<string>? requested, string[]? configured)
    {
        IEnumerable<string> source = Array.Empty<string>();

        if (requested is not null && requested.Count > 0)
        {
            source = requested;
        }
        else if (configured is not null && configured.Length > 0)
        {
            source = configured;
        }

        return source
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(NormalizeClient)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeClient(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return "client";
        }

        var builder = new StringBuilder(trimmed.Length);
        var previousHyphen = false;

        foreach (var ch in trimmed)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
                previousHyphen = false;
            }
            else if (ch is '-' or '_')
            {
                builder.Append(ch);
                previousHyphen = false;
            }
            else if (char.IsWhiteSpace(ch) || ch == '.')
            {
                if (!previousHyphen)
                {
                    builder.Append('-');
                    previousHyphen = true;
                }
            }
        }

        var normalized = builder.ToString().Trim('-');
        if (string.IsNullOrEmpty(normalized) || normalized is "." or "..")
        {
            return "client";
        }

        return normalized;
    }

    private byte[] BuildAppSettings(KoanAdminManifest manifest, string profile, DateTimeOffset generatedAt)
    {
        var modules = manifest.Modules
            .Select(m => new
            {
                name = m.Name,
                version = m.Version,
                settings = m.Settings
                    .Where(s => !s.Secret)
                    .ToDictionary(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase),
                notes = m.Notes
            })
            .ToList();

        var document = new
        {
            schema = "https://koan.dev/schemas/launchkit/appsettings.json",
            generatedAtUtc = generatedAt,
            environment = _environment.EnvironmentName,
            profile,
            modules
        };

        return JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
    }

    private byte[] BuildCompose(KoanAdminManifest manifest, string profile, int basePort)
    {
        var builder = new StringBuilder();
        builder.AppendLine("version: '3.9'");
        builder.AppendLine($"name: koan-{Slugify(profile)}");
        builder.AppendLine("services:");

        var port = Math.Max(basePort, 0);
        foreach (var module in manifest.Modules)
        {
            var serviceName = Slugify(module.Name);
            builder.AppendLine($"  {serviceName}:");
            builder.AppendLine($"    image: koan/{serviceName}:latest");
            builder.AppendLine("    environment:");
            builder.AppendLine($"      - KOAN_MODULE={module.Name}");
            if (!string.IsNullOrWhiteSpace(module.Version))
            {
                builder.AppendLine("    labels:");
                builder.AppendLine($"      koan.module.version: \"{module.Version}\"");
            }
            builder.AppendLine("    ports:");
            builder.AppendLine($"      - \"{port + 1}:8080\"");
            port += 10;
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private byte[] BuildAspire(KoanAdminManifest manifest, string profile)
    {
        var modules = manifest.Modules
            .Select(m => new
            {
                name = m.Name,
                image = $"{Slugify(m.Name)}:latest",
                version = m.Version ?? "latest"
            });

        var document = new
        {
            schemaVersion = "1.0",
            name = $"koan-{Slugify(profile)}",
            services = modules
        };

        return JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
    }

    private byte[] BuildMetadata(
        KoanAdminManifest manifest,
        string profile,
        DateTimeOffset generatedAt,
        KoanAdminRouteMap routes,
        bool includeAppSettings,
        bool includeCompose,
        bool includeAspire,
        bool includeManifest,
        IReadOnlyList<string> openApiClients)
    {
        var document = new
        {
            profile,
            generatedAtUtc = generatedAt,
            environment = _environment.EnvironmentName,
            configuration = new
            {
                includeAppSettings,
                includeCompose,
                includeAspire,
                includeManifest,
                openApiClients
            },
            routes = new
            {
                routes.RootPath,
                routes.ApiPath,
                routes.LaunchKitPath,
                routes.ManifestPath,
                routes.HealthPath
            },
            modules = manifest.Modules.Select(m => new
            {
                m.Name,
                m.Version,
                Settings = m.Settings.Count,
                Notes = m.Notes.Count
            })
        };

        return JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
    }

    private static string BuildReadme(
        KoanAdminManifest manifest,
        string profile,
        DateTimeOffset generatedAt,
        KoanAdminRouteMap routes)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Koan LaunchKit Bundle");
        builder.AppendLine();
        builder.AppendLine($"- **Profile**: `{profile}`");
        builder.AppendLine($"- **Generated At (UTC)**: `{generatedAt:O}`");
        builder.AppendLine($"- **Admin Root**: `{routes.RootPath}`");
        builder.AppendLine($"- **Admin API**: `{routes.ApiPath}`");
        builder.AppendLine();
        builder.AppendLine("## Modules");
        foreach (var module in manifest.Modules)
        {
            var version = string.IsNullOrWhiteSpace(module.Version) ? "(unversioned)" : module.Version;
            builder.AppendLine($"- `{module.Name}` — version {version}, {module.Settings.Count} settings, {module.Notes.Count} notes");
        }

        builder.AppendLine();
        builder.AppendLine("## Next Steps");
        builder.AppendLine("1. Update secrets within `config/appsettings.*.json` before sharing.");
        builder.AppendLine("2. Review `compose/` for container image names and port assignments.");
        builder.AppendLine("3. Import `aspire/` definitions into your Aspire app host if applicable.");
        builder.AppendLine("4. Use the OpenAPI instructions under `openapi/` to generate client SDKs.");

        return builder.ToString();
    }

    private string BuildOpenApiInstructions(string client, string profile, KoanAdminRouteMap routes)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# OpenAPI Client ({client})");
        builder.AppendLine();
        builder.AppendLine("These instructions help bootstrap client SDK generation for the current host.");
        builder.AppendLine();
        builder.AppendLine("## Suggested Workflow");
        builder.AppendLine("1. Ensure the host is running and the Swagger document is reachable.");
        builder.AppendLine("2. Replace `<base-address>` with your running instance URI.");
        builder.AppendLine("3. Run the commands below.");
        builder.AppendLine();
        builder.AppendLine("```bash");
        if (string.Equals(client, "csharp", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("dotnet tool install --global NSwag.Console --version 14.*");
            builder.AppendLine($"nswag openapi2csclient /input:<base-address>/swagger/v1/swagger.json /classname:KoanClient /namespace:Koan.Generated.Clients.{Slugify(profile)}");
        }
        else if (string.Equals(client, "typescript", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("npx @openapitools/openapi-generator-cli generate \\");
            builder.AppendLine("  -i <base-address>/swagger/v1/swagger.json \\");
            builder.AppendLine($"  -g typescript-axios \\");
            builder.AppendLine($"  -o ./openapi/{client}/dist");
        }
        else
        {
            builder.AppendLine($"# Install your preferred OpenAPI generator for '{client}'.");
            builder.AppendLine($"openapi-generator generate -i <base-address>/swagger/v1/swagger.json -g {client} -o ./openapi/{client}/dist");
        }
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine($"Admin API base path: `{routes.ApiPath}`");

        return builder.ToString();
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
            else if (ch is '-' or '_' or '.')
            {
                builder.Append(ch);
            }
            else if (char.IsWhiteSpace(ch))
            {
                builder.Append('-');
            }
        }

        return builder.Length == 0 ? "koan" : builder.ToString();
    }

    private sealed record FileBuffer(string Path, string ContentType, byte[] Content);
}
