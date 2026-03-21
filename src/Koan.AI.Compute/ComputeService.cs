using System.Runtime.InteropServices;
using Koan.AI.Contracts.Shared;

namespace Koan.AI.Compute;

/// <summary>
/// Default implementation of <see cref="IComputeService"/>.
/// Discovers local compute resources (CPU, installed runtimes) and resolves
/// workload placement. Network compute via ZenGarden is deferred to a future release.
/// </summary>
internal sealed class ComputeService : IComputeService
{
    private readonly Lazy<ComputeResource> _localResource;

    public ComputeService()
    {
        _localResource = new Lazy<ComputeResource>(DetectLocalCompute);
    }

    public Task<ComputeResource?> AvailableAsync(CancellationToken ct = default)
    {
        _ = ct;
        return Task.FromResult<ComputeResource?>(_localResource.Value);
    }

    public Task<IReadOnlyList<ComputeResource>> FleetAsync(CancellationToken ct = default)
    {
        _ = ct;
        IReadOnlyList<ComputeResource> fleet = [_localResource.Value];
        return Task.FromResult(fleet);
    }

    public Task<ComputeResolution> ResolveAsync(ComputeRequirement requirement, CancellationToken ct = default)
    {
        _ = ct;
        var local = _localResource.Value;

        if (local.Satisfies(requirement))
        {
            var resolution = new ComputeResolution
            {
                Target = local,
                Reason = "Local compute satisfies requirement.",
                Alternatives = [],
                LocalFallback = null
            };
            return Task.FromResult(resolution);
        }

        // Local cannot satisfy — return it as a fallback with an explanatory reason.
        var fallbackResolution = new ComputeResolution
        {
            Target = local,
            Reason = "No compute resource fully satisfies the requirement; " +
                     "falling back to local CPU. Network compute is not yet available.",
            Alternatives = [],
            LocalFallback = local
        };
        return Task.FromResult(fallbackResolution);
    }

    public Task<bool> CheckAsync(ReadinessSpec spec, CancellationToken ct = default)
    {
        _ = ct;

        // Network readiness cannot be checked without ZenGarden integration.
        if (spec.NetworkRequired)
            return Task.FromResult(false);

        // Stub: capability checks require model registry integration.
        // For now, only local inference is considered available.
        var localCapabilities = new[] { ComputeCapability.Inference };
        var allCapabilitiesMet = spec.RequiredCapabilities
            .All(c => localCapabilities.Contains(c));

        return Task.FromResult(allCapabilitiesMet && spec.RequiredModels.Length == 0);
    }

    // ── Local Detection ──

    private static ComputeResource DetectLocalCompute()
    {
        var isContainer = string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        var deviceName = BuildDeviceName(isContainer);

        return new ComputeResource
        {
            Id = "local",
            Accelerator = Accelerator.None,
            VramBytes = 0,
            DeviceName = deviceName,
            Location = ComputeLocation.Local,
            Runtimes = DetectRuntimes(),
            StoneId = null,
            Status = ComputeStatus.Available
        };
    }

    private static string BuildDeviceName(bool isContainer)
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS"
            : "Unknown";

        var arch = RuntimeInformation.ProcessArchitecture.ToString();
        var suffix = isContainer ? " (container)" : "";

        return $"CPU · {os} {arch}{suffix}";
    }

    private static string[] DetectRuntimes()
    {
        var runtimes = new List<string>();

        // Ollama: check environment variable or well-known binary locations.
        if (Environment.GetEnvironmentVariable("OLLAMA_HOST") is not null
            || File.Exists("/usr/local/bin/ollama")
            || File.Exists("/usr/bin/ollama"))
        {
            runtimes.Add("ollama");
        }

        // Python: check PATH for python3 or python.
        if (IsInPath("python3") || IsInPath("python"))
        {
            runtimes.Add("python");
        }

        // ONNX Runtime: check if the assembly is already loaded.
        var onnxLoaded = AppDomain.CurrentDomain.GetAssemblies()
            .Any(a => a.GetName().Name == "Microsoft.ML.OnnxRuntime");
        if (onnxLoaded)
        {
            runtimes.Add("onnxruntime");
        }

        return runtimes.ToArray();
    }

    private static bool IsInPath(string executable)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar))
            return false;

        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        var extensions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { ".exe", ".cmd", ".bat" }
            : Array.Empty<string>();

        foreach (var dir in pathVar.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(Path.Combine(dir, executable)))
                return true;

            foreach (var ext in extensions)
            {
                if (File.Exists(Path.Combine(dir, executable + ext)))
                    return true;
            }
        }

        return false;
    }
}
