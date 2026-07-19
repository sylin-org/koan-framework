using System.Globalization;
using Koan.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Koan.Observability.Infrastructure;

internal sealed record ObservabilityPlan(
    bool Active,
    bool TracesEnabled,
    bool MetricsEnabled,
    double TraceSampleRate,
    Uri? OtlpEndpoint,
    string? OtlpHeaders,
    string ServiceName,
    string ServiceVersion,
    string ServiceInstanceId,
    string StatusDetail)
{
    public string Exporter => OtlpEndpoint is null ? "none" : "otlp";

    public static ObservabilityPlan Compile(IConfiguration? configuration, IHostEnvironment? environment)
    {
        var requested = ReadBoolean(configuration, Constants.Configuration.Enabled, true);
        var traces = ReadBoolean(configuration, Constants.Configuration.TracesEnabled, true);
        var metrics = ReadBoolean(configuration, Constants.Configuration.MetricsEnabled, true);
        var sampleRate = ReadSampleRate(configuration);
        var endpointText = Read(configuration, Constants.Configuration.OtlpEndpoint)
            ?? Read(configuration, Koan.Core.Infrastructure.Constants.Configuration.Otel.Exporter.Otlp.Endpoint);
        var headers = Read(configuration, Constants.Configuration.OtlpHeaders)
            ?? Read(configuration, Koan.Core.Infrastructure.Constants.Configuration.Otel.Exporter.Otlp.Headers);

        var production = environment?.IsProduction() ?? KoanEnv.IsProduction;
        var active = requested && (traces || metrics) && (!production || !string.IsNullOrWhiteSpace(endpointText));
        var endpoint = active ? ParseEndpoint(endpointText) : null;
        var entry = System.Reflection.Assembly.GetEntryAssembly();
        var serviceName = environment?.ApplicationName
            ?? entry?.GetName().Name
            ?? "Koan-app";
        var serviceVersion = entry?.GetName().Version?.ToString() ?? "0.0.0";

        var detail = !requested
            ? $"inactive: {Constants.Configuration.Enabled}=false"
            : !traces && !metrics
                ? "inactive: traces and metrics are disabled"
                : production && string.IsNullOrWhiteSpace(endpointText)
                    ? $"inactive: Production requires {Constants.Configuration.OtlpEndpoint} or OTEL_EXPORTER_OTLP_ENDPOINT"
                    : $"active: traces={traces.ToString().ToLowerInvariant()}, metrics={metrics.ToString().ToLowerInvariant()}, exporter={(endpoint is null ? "none" : "otlp")}";

        return new ObservabilityPlan(
            active,
            active && traces,
            active && metrics,
            sampleRate,
            endpoint,
            string.IsNullOrWhiteSpace(headers) ? null : headers,
            serviceName,
            serviceVersion,
            Environment.MachineName,
            detail);
    }

    private static bool ReadBoolean(IConfiguration? configuration, string key, bool fallback)
    {
        var value = Read(configuration, key);
        if (value is null) return fallback;
        if (bool.TryParse(value, out var parsed)) return parsed;

        throw new InvalidOperationException(
            $"Koan Observability configuration '{key}' must be 'true' or 'false'; received '{value}'.");
    }

    private static double ReadSampleRate(IConfiguration? configuration)
    {
        var value = Read(configuration, Constants.Configuration.TraceSampleRate);
        if (value is null) return 0.1;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && double.IsFinite(parsed)
            && parsed is >= 0 and <= 1)
        {
            return parsed;
        }

        throw new InvalidOperationException(
            $"Koan Observability configuration '{Constants.Configuration.TraceSampleRate}' must be a number from 0 to 1; received '{value}'.");
    }

    private static Uri? ParseEndpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (Uri.TryCreate(value, UriKind.Absolute, out var endpoint)
            && (endpoint.Scheme == Uri.UriSchemeHttp || endpoint.Scheme == Uri.UriSchemeHttps))
        {
            return endpoint;
        }

        throw new InvalidOperationException(
            $"Koan Observability configuration '{Constants.Configuration.OtlpEndpoint}' must be an absolute HTTP or HTTPS URI; received '{value}'.");
    }

    private static string? Read(IConfiguration? configuration, string key)
        => Koan.Core.Configuration.Read<string?>(configuration, key, null);
}
