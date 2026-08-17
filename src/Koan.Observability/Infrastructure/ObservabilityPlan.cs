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
    public string Exporter => DescribeExporter(OtlpEndpoint);

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

        var detail = DescribeStatus(requested, traces, metrics, production, endpointText, endpoint);

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

    // One owner for the exporter name. It is reported both as plan state and inside the status
    // detail, and those two answers must never be able to disagree.
    private static string DescribeExporter(Uri? endpoint) => endpoint is null ? "none" : "otlp";

    private static string DescribeStatus(
        bool requested,
        bool traces,
        bool metrics,
        bool production,
        string? endpointText,
        Uri? endpoint)
    {
        if (!requested) return $"inactive: {Constants.Configuration.Enabled}=false";
        if (!traces && !metrics) return "inactive: traces and metrics are disabled";
        if (production && string.IsNullOrWhiteSpace(endpointText))
        {
            return $"inactive: Production requires {Constants.Configuration.OtlpEndpoint} or OTEL_EXPORTER_OTLP_ENDPOINT";
        }

        return $"active: traces={Format(traces)}, metrics={Format(metrics)}, exporter={DescribeExporter(endpoint)}";

        static string Format(bool value) => value ? "true" : "false";
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
