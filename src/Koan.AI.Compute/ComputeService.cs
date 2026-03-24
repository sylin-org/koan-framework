using System.Diagnostics;
using System.Runtime.InteropServices;
using Koan.AI.Contracts.Shared;

namespace Koan.AI.Compute;

/// <summary>
/// Default implementation of <see cref="IComputeService"/>.
/// Discovers local compute resources (CPU, GPU, installed runtimes) and resolves
/// workload placement. Network compute via ZenGarden is deferred to a future release.
/// </summary>
internal sealed class ComputeService : IComputeService
{
    private readonly Lazy<ComputeResource> _localResource;

    public ComputeService()
    {
        _localResource = new Lazy<ComputeResource>(DetectLocalCompute);
    }

    public Task<ComputeResource?> Available(CancellationToken ct = default)
    {
        _ = ct;
        return Task.FromResult<ComputeResource?>(_localResource.Value);
    }

    public Task<IReadOnlyList<ComputeResource>> Fleet(CancellationToken ct = default)
    {
        _ = ct;
        IReadOnlyList<ComputeResource> fleet = [_localResource.Value];
        return Task.FromResult(fleet);
    }

    public Task<ComputeResolution> Resolve(ComputeRequirement requirement, CancellationToken ct = default)
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

    public Task<bool> Check(ReadinessSpec spec, CancellationToken ct = default)
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

        var gpu = DetectGpu();
        var deviceName = BuildDeviceName(isContainer, gpu);

        return new ComputeResource
        {
            Id = "local",
            Accelerator = gpu.Accelerator,
            VramBytes = gpu.VramBytes,
            DeviceName = deviceName,
            Location = ComputeLocation.Local,
            Runtimes = DetectRuntimes(),
            StoneId = null,
            Status = ComputeStatus.Available
        };
    }

    private static string BuildDeviceName(bool isContainer, GpuInfo gpu)
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS"
            : "Unknown";

        var arch = RuntimeInformation.ProcessArchitecture.ToString();
        var suffix = isContainer ? " (container)" : "";

        var deviceLabel = gpu.Accelerator != Accelerator.None && gpu.Name is not null
            ? $"{gpu.Name} · {os} {arch}{suffix}"
            : $"CPU · {os} {arch}{suffix}";

        return deviceLabel;
    }

    // ── GPU Detection ──

    private readonly record struct GpuInfo(Accelerator Accelerator, long VramBytes, string? Name);

    private static GpuInfo DetectGpu()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return DetectGpuWindows();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return DetectGpuLinux();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return DetectGpuMacOS();

        return new GpuInfo(Accelerator.None, 0, null);
    }

    private static GpuInfo DetectGpuWindows()
    {
        // Try nvidia-smi first (CUDA toolkit installed).
        var nvidiaSmi = TryRunProcess("nvidia-smi",
            "--query-gpu=name,memory.total --format=csv,noheader,nounits");
        if (nvidiaSmi is not null)
        {
            var parts = nvidiaSmi.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length >= 2)
            {
                var name = parts[0];
                var vramMb = long.TryParse(parts[1], out var mb) ? mb : 0;
                return new GpuInfo(Accelerator.CUDA, vramMb * 1024 * 1024, name);
            }
        }

        // Check environment variables for GPU presence.
        if (Environment.GetEnvironmentVariable("CUDA_VISIBLE_DEVICES") is not null)
            return new GpuInfo(Accelerator.CUDA, 0, "NVIDIA GPU (CUDA)");

        if (Environment.GetEnvironmentVariable("HIP_VISIBLE_DEVICES") is not null)
            return new GpuInfo(Accelerator.DirectML, 0, "AMD GPU (ROCm/DirectML)");

        // Try WMI via PowerShell as a lightweight alternative to System.Management.
        var wmiResult = TryRunProcess("powershell", "-NoProfile -Command \"" +
            "Get-CimInstance Win32_VideoController | " +
            "Select-Object -First 1 -ExpandProperty Name\"");
        if (wmiResult is not null)
        {
            var gpuName = wmiResult.Trim();
            if (!string.IsNullOrEmpty(gpuName))
            {
                var accelerator = ClassifyGpuVendor(gpuName, isLinux: false);
                if (accelerator != Accelerator.None)
                {
                    // Try to get adapter RAM.
                    var ramResult = TryRunProcess("powershell", "-NoProfile -Command \"" +
                        "Get-CimInstance Win32_VideoController | " +
                        "Select-Object -First 1 -ExpandProperty AdapterRAM\"");
                    var vram = ramResult is not null && long.TryParse(ramResult.Trim(), out var bytes)
                        ? bytes : 0;

                    return new GpuInfo(accelerator, vram, gpuName);
                }
            }
        }

        return new GpuInfo(Accelerator.None, 0, null);
    }

    private static GpuInfo DetectGpuLinux()
    {
        // Try nvidia-smi for NVIDIA GPUs.
        var nvidiaSmi = TryRunProcess("nvidia-smi",
            "--query-gpu=name,memory.total --format=csv,noheader,nounits");
        if (nvidiaSmi is not null)
        {
            var parts = nvidiaSmi.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length >= 2)
            {
                var name = parts[0];
                var vramMb = long.TryParse(parts[1], out var mb) ? mb : 0;
                return new GpuInfo(Accelerator.CUDA, vramMb * 1024 * 1024, name);
            }
        }

        // Check NVIDIA GPU driver files.
        if (Directory.Exists("/proc/driver/nvidia/gpus"))
        {
            try
            {
                var gpuDirs = Directory.GetDirectories("/proc/driver/nvidia/gpus");
                if (gpuDirs.Length > 0)
                {
                    var infoPath = Path.Combine(gpuDirs[0], "information");
                    if (File.Exists(infoPath))
                    {
                        var info = File.ReadAllText(infoPath);
                        var nameLine = info.Split('\n')
                            .FirstOrDefault(l => l.StartsWith("Model:", StringComparison.OrdinalIgnoreCase));
                        var name = nameLine?.Split(':', 2).ElementAtOrDefault(1)?.Trim() ?? "NVIDIA GPU";
                        return new GpuInfo(Accelerator.CUDA, 0, name);
                    }
                }
            }
            catch
            {
                // Permission denied or other filesystem error — fall through.
            }
        }

        // Check environment variables.
        if (Environment.GetEnvironmentVariable("CUDA_VISIBLE_DEVICES") is not null)
            return new GpuInfo(Accelerator.CUDA, 0, "NVIDIA GPU (CUDA)");

        if (Environment.GetEnvironmentVariable("HIP_VISIBLE_DEVICES") is not null)
            return new GpuInfo(Accelerator.ROCm, 0, "AMD GPU (ROCm)");

        // Check AMD via sysfs DRM vendor ID.
        try
        {
            if (Directory.Exists("/sys/class/drm"))
            {
                foreach (var cardDir in Directory.GetDirectories("/sys/class/drm", "card*"))
                {
                    var vendorPath = Path.Combine(cardDir, "device", "vendor");
                    if (File.Exists(vendorPath))
                    {
                        var vendor = File.ReadAllText(vendorPath).Trim();
                        if (vendor == "0x1002") // AMD vendor ID
                            return new GpuInfo(Accelerator.ROCm, 0, "AMD GPU");
                    }
                }
            }
        }
        catch
        {
            // Permission denied — fall through.
        }

        return new GpuInfo(Accelerator.None, 0, null);
    }

    private static GpuInfo DetectGpuMacOS()
    {
        // Apple Silicon detection via architecture.
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            // Read unified memory size via sysctl.
            var memResult = TryRunProcess("sysctl", "-n hw.memsize");
            var memBytes = memResult is not null && long.TryParse(memResult.Trim(), out var bytes)
                ? bytes : 0;

            return new GpuInfo(Accelerator.Metal, memBytes, "Apple Silicon (Metal)");
        }

        return new GpuInfo(Accelerator.None, 0, null);
    }

    private static Accelerator ClassifyGpuVendor(string gpuName, bool isLinux)
    {
        var upper = gpuName.ToUpperInvariant();

        if (upper.Contains("NVIDIA") || upper.Contains("GEFORCE") || upper.Contains("QUADRO") || upper.Contains("RTX") || upper.Contains("GTX"))
            return isLinux ? Accelerator.CUDA : HasCudaToolkit() ? Accelerator.CUDA : Accelerator.DirectML;

        if (upper.Contains("AMD") || upper.Contains("RADEON") || upper.Contains("RX "))
            return isLinux ? Accelerator.ROCm : Accelerator.DirectML;

        if (upper.Contains("INTEL"))
            return isLinux ? Accelerator.OneAPI : Accelerator.DirectML;

        return Accelerator.None;
    }

    private static bool HasCudaToolkit()
    {
        // Check for nvcc in PATH or CUDA_PATH environment variable.
        return Environment.GetEnvironmentVariable("CUDA_PATH") is not null
            || IsInPath("nvcc");
    }

    private static string? TryRunProcess(string fileName, string arguments)
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(TimeSpan.FromSeconds(5));

            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    // ── Runtime Detection ──

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
