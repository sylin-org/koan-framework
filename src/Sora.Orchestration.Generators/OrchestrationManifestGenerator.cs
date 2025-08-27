using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Sora.Orchestration.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class OrchestrationManifestGenerator : ISourceGenerator
{
    private const string ServiceIdAttr = "Sora.Orchestration.Abstractions.Attributes.ServiceIdAttribute";
    private const string ContainerDefaultsAttr = "Sora.Orchestration.Abstractions.Attributes.ContainerDefaultsAttribute";
    private const string EndpointDefaultsAttr = "Sora.Orchestration.Abstractions.Attributes.EndpointDefaultsAttribute";
    private const string AppEnvDefaultsAttr = "Sora.Orchestration.Abstractions.Attributes.AppEnvDefaultsAttribute";
    private const string HealthDefaultsAttr = "Sora.Orchestration.Abstractions.Attributes.HealthEndpointDefaultsAttribute";

    public void Initialize(GeneratorInitializationContext context) { }

    public void Execute(GeneratorExecutionContext context)
    {
        try
        {
            var candidates = new List<ServiceCandidate>();
            foreach (var tree in context.Compilation.SyntaxTrees)
            {
                var sm = context.Compilation.GetSemanticModel(tree, ignoreAccessibility: true);
                var root = tree.GetRoot(context.CancellationToken);
                foreach (var decl in root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ClassDeclarationSyntax>())
                {
                    var sym = sm.GetDeclaredSymbol(decl, context.CancellationToken) as INamedTypeSymbol;
                    if (sym is null || sym.IsAbstract) continue;

                    string? sid = null; string? image = null; string? tag = null;
                    var ports = new List<int>();
                    var env = new Dictionary<string, string?>();
                    var appEnv = new Dictionary<string, string?>();
                    var volumes = new List<string>();
                    string? scheme = null; string? host = null; int? endpointPort = null; string? uriPattern = null;
                    string? localScheme = null; string? localHost = null; int? localPort = null; string? localPattern = null;
                    string? healthPath = null; int? healthInterval = null; int? healthTimeout = null; int? healthRetries = null;

                    foreach (var a in sym.GetAttributes())
                    {
                        var full = a.AttributeClass?.ToDisplayString();
                        if (string.Equals(full, ServiceIdAttr, StringComparison.Ordinal))
                        {
                            if (a.ConstructorArguments.Length > 0)
                                sid = a.ConstructorArguments[0].Value?.ToString();
                        }
                        else if (string.Equals(full, ContainerDefaultsAttr, StringComparison.Ordinal))
                        {
                            if (a.ConstructorArguments.Length > 0)
                                image = a.ConstructorArguments[0].Value?.ToString();
                            foreach (var na in a.NamedArguments)
                            {
                                switch (na.Key)
                                {
                                    case "Tag": tag = na.Value.Value?.ToString(); break;
                                    case "Ports":
                                        if (na.Value.Values is { Length: > 0 })
                                            ports.AddRange(na.Value.Values.Select(v => (int)(v.Value ?? 0)));
                                        break;
                                    case "Env":
                                        if (na.Value.Values is { Length: > 0 })
                                            foreach (var v in na.Value.Values) AddKv(env, v.Value?.ToString());
                                        break;
                                    case "Volumes":
                                        if (na.Value.Values is { Length: > 0 })
                                            volumes.AddRange(na.Value.Values.Select(v => v.Value?.ToString()).Where(v => !string.IsNullOrEmpty(v))!);
                                        break;
                                }
                            }
                        }
                        else if (string.Equals(full, EndpointDefaultsAttr, StringComparison.Ordinal))
                        {
                            // Prefer Container mode: handle both enum textual and underlying integral representations
                            if (a.ConstructorArguments.Length >= 4)
                            {
                                var modeArg = a.ConstructorArguments[0];
                                var isContainer = false;
                                try
                                {
                                    if (modeArg.Value is int i)
                                    {
                                        // EndpointMode.Container == 0
                                        isContainer = i == 0;
                                    }
                                    else
                                    {
                                        var s = modeArg.Value?.ToString() ?? string.Empty;
                                        isContainer = s.IndexOf("Container", StringComparison.OrdinalIgnoreCase) >= 0;
                                    }
                                }
                                catch { /* be permissive */ }

                                if (isContainer)
                                {
                                    scheme = a.ConstructorArguments[1].Value?.ToString();
                                    host = a.ConstructorArguments[2].Value?.ToString();
                                    endpointPort = (int?)a.ConstructorArguments[3].Value;
                                    foreach (var na in a.NamedArguments)
                                    {
                                        if (na.Key == "UriPattern") uriPattern = na.Value.Value?.ToString();
                                    }
                                }
                                else
                                {
                                    localScheme = a.ConstructorArguments[1].Value?.ToString();
                                    localHost = a.ConstructorArguments[2].Value?.ToString();
                                    localPort = (int?)a.ConstructorArguments[3].Value;
                                    foreach (var na in a.NamedArguments)
                                    {
                                        if (na.Key == "UriPattern") localPattern = na.Value.Value?.ToString();
                                    }
                                }
                            }
                        }
                        else if (string.Equals(full, HealthDefaultsAttr, StringComparison.Ordinal))
                        {
                            if (a.ConstructorArguments.Length > 0)
                                healthPath = a.ConstructorArguments[0].Value?.ToString();
                            foreach (var na in a.NamedArguments)
                            {
                                switch (na.Key)
                                {
                                    case "IntervalSeconds": healthInterval = (int?)na.Value.Value; break;
                                    case "TimeoutSeconds": healthTimeout = (int?)na.Value.Value; break;
                                    case "Retries": healthRetries = (int?)na.Value.Value; break;
                                }
                            }
                        }
                        else if (string.Equals(full, AppEnvDefaultsAttr, StringComparison.Ordinal))
                        {
                            if (a.ConstructorArguments.Length > 0)
                            {
                                foreach (var v in a.ConstructorArguments[0].Values)
                                    AddKv(appEnv, v.Value?.ToString());
                            }
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(sid) && !string.IsNullOrWhiteSpace(image))
                    {
                        if (!string.IsNullOrEmpty(tag)) image = image + ":" + tag;
                        // If endpoint port from EndpointDefaults not provided, fall back to first container port
                        if (endpointPort is null && ports.Count > 0) endpointPort = ports[0];
                        candidates.Add(new ServiceCandidate(sid!, image!, ports.ToArray(), env, volumes.ToArray(), appEnv,
                            scheme, host, endpointPort, uriPattern, localScheme, localHost, localPort, localPattern,
                            healthPath, healthInterval, healthTimeout, healthRetries));
                    }
                }
            }

            if (candidates.Count == 0) return;

            var json = BuildJson(candidates);
            var src = "namespace Sora.Orchestration { public static class __SoraOrchestrationManifest { public const string Json = \"" + Escape(json) + "\"; } }";
            context.AddSource("__SoraOrchestrationManifest.g.cs", SourceText.From(src, Encoding.UTF8));
        }
        catch
        {
            // Swallow generator errors to avoid breaking builds
        }
    }

    private static string BuildJson(List<ServiceCandidate> items)
    {
        var sb = new StringBuilder();
        sb.Append('{').Append("\"services\": [");
        for (int i = 0; i < items.Count; i++)
        {
            var s = items[i];
            if (i > 0) sb.Append(',');
            sb.Append('{')
                .Append(Prop("id", s.Id)).Append(',')
                .Append(Prop("image", s.Image)).Append(',')
                .Append(Prop("ports", s.Ports))
                .Append(',').Append(Prop("env", s.Env))
                .Append(',').Append(Prop("volumes", s.Volumes))
                .Append(',').Append(Prop("appEnv", s.AppEnv));
            if (!string.IsNullOrEmpty(s.Scheme)) sb.Append(',').Append(Prop("scheme", s.Scheme!));
            if (!string.IsNullOrEmpty(s.Host)) sb.Append(',').Append(Prop("host", s.Host!));
            if (s.EndpointPort is int ep) sb.Append(',').Append(Prop("endpointPort", ep));
            if (!string.IsNullOrEmpty(s.UriPattern)) sb.Append(',').Append(Prop("uriPattern", s.UriPattern!));
            if (!string.IsNullOrEmpty(s.LocalScheme)) sb.Append(',').Append(Prop("localScheme", s.LocalScheme!));
            if (!string.IsNullOrEmpty(s.LocalHost)) sb.Append(',').Append(Prop("localHost", s.LocalHost!));
            if (s.LocalPort is int lp) sb.Append(',').Append(Prop("localPort", lp));
            if (!string.IsNullOrEmpty(s.LocalPattern)) sb.Append(',').Append(Prop("localPattern", s.LocalPattern!));
            if (!string.IsNullOrEmpty(s.HealthPath)) sb.Append(',').Append(Prop("healthPath", s.HealthPath!));
            if (s.HealthInterval is int hi) sb.Append(',').Append(Prop("healthInterval", hi));
            if (s.HealthTimeout is int ht) sb.Append(',').Append(Prop("healthTimeout", ht));
            if (s.HealthRetries is int hr) sb.Append(',').Append(Prop("healthRetries", hr));
            sb.Append('}');
        }
        sb.Append("]}");
        return sb.ToString();
    }

    private static string Prop(string name, string value) => "\"" + name + "\": \"" + Escape(value) + "\"";
    private static string Prop(string name, int value) => "\"" + name + "\": " + value;
    private static string Prop(string name, int[] values) => "\"" + name + "\": [" + string.Join(",", values) + "]";
    private static string Prop(string name, string[] values) => "\"" + name + "\": [" + string.Join(",", values.Select(v => "\"" + Escape(v) + "\"")) + "]";
    private static string Prop(string name, Dictionary<string, string?> map) => "\"" + name + "\": " + FormatMap(map);

    private static string FormatMap(Dictionary<string,string?> map)
        => "{" + string.Join(",", map.Select(kv => "\"" + Escape(kv.Key) + "\": \"" + Escape(kv.Value ?? string.Empty) + "\"")) + "}";

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static void AddKv(Dictionary<string,string?> target, string? kv)
    {
        if (string.IsNullOrWhiteSpace(kv)) return;
        var idx = kv.IndexOf('=');
        if (idx > 0)
        {
            var key = kv.Substring(0, idx);
            var val = kv.Substring(idx + 1);
            target[key] = val;
        }
    }

    private sealed class ServiceCandidate
    {
        public string Id { get; }
        public string Image { get; }
        public int[] Ports { get; }
        public Dictionary<string, string?> Env { get; }
        public string[] Volumes { get; }
        public Dictionary<string, string?> AppEnv { get; }
        public string? Scheme { get; }
        public string? Host { get; }
    public int? EndpointPort { get; }
    public string? UriPattern { get; }
    public string? LocalScheme { get; }
    public string? LocalHost { get; }
    public int? LocalPort { get; }
    public string? LocalPattern { get; }
    public string? HealthPath { get; }
    public int? HealthInterval { get; }
    public int? HealthTimeout { get; }
    public int? HealthRetries { get; }

        public ServiceCandidate(
            string id,
            string image,
            int[] ports,
            Dictionary<string, string?> env,
            string[] volumes,
            Dictionary<string, string?> appEnv,
            string? scheme,
            string? host,
            int? endpointPort,
            string? uriPattern,
            string? localScheme,
            string? localHost,
            int? localPort,
            string? localPattern,
            string? healthPath,
            int? healthInterval,
            int? healthTimeout,
            int? healthRetries)
        {
            Id = id;
            Image = image;
            Ports = ports;
            Env = env;
            Volumes = volumes;
            AppEnv = appEnv;
            Scheme = scheme;
            Host = host;
            EndpointPort = endpointPort;
            UriPattern = uriPattern;
            LocalScheme = localScheme;
            LocalHost = localHost;
            LocalPort = localPort;
            LocalPattern = localPattern;
            HealthPath = healthPath;
            HealthInterval = healthInterval;
            HealthTimeout = healthTimeout;
            HealthRetries = healthRetries;
        }
    }
}
