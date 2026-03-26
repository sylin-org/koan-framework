---
id: AI-0024
slug: AI-0024-compute-fabric
domain: AI
status: Accepted
date: 2026-03-20
implementation: "Implemented in src/Koan.AI.Compute/ with Compute.* facade"
---

# ADR: Compute Fabric — Hardware-Agnostic Compute Discovery and Routing

**Contract**

- **Inputs:** ZenGarden topology (stone capabilities), local OS hardware queries (WMI/sysfs/IOKit), runtime probes (Ollama, ONNX Runtime, Python+PyTorch, Docker/Podman), `ComputeRequirement` from callers (Training, Model.Convert, Eval), `Koan:Ai:Compute` configuration section.
- **Outputs:** `Compute.*` facade with discovery (`Available`, `Fleet`, `Forecast`), resolution (`Resolve`, `Require`, `Prefer`, `Check`), and management (`Update`) verbs; `ComputeResource` inventory; `ComputeResolution` advisory results; `Koan.AI.Worker` hosted service for GPU-capable machines; filesystem-based job delegation protocol; boot report section describing discovered compute.
- **Error Modes:** No GPU anywhere: CPU fallback with performance warning. ZenGarden unreachable: local-only mode with diagnostic in boot report. Worker offline mid-job: checkpoint preserves progress; retry on reconnect. Accelerator mismatch (e.g., ROCm requested, only CUDA available): reject with diagnostic listing available accelerators. VRAM insufficient everywhere: reject with capacity report showing requirements vs availability.
- **Acceptance Criteria:** A developer on a laptop with 8 GB GPU can `Compute.Available()` to see local + network resources, `Compute.Resolve(workload)` to get a routing recommendation, and have `Training.Train()` transparently delegate to a network A100 via ZenGarden — all without writing provider-specific code. Air-gapped environments with no ZenGarden operate in local-only mode with full discovery and resolution functionality.

**Edge Cases**

- No GPU on any reachable node: Resolution returns `ComputeResolution` with `Target` set to CPU local, `Reason` explaining no accelerator found, and `Warnings` containing performance impact estimate.
- ZenGarden unreachable at startup: Discovery completes with local resources only. Boot report shows `Network: Unavailable (ZenGarden not configured or unreachable)`. All `Compute.*` verbs remain functional for local compute.
- Multiple GPUs on a single node: Each GPU is a distinct `ComputeResource` with its own Id, VRAM, and utilization. Resolution considers per-GPU availability, not per-node.
- GPU busy (loaded model consuming VRAM): `ComputeStatus.Busy` reported with `AvailableVramBytes` reflecting actual free VRAM, not total. Resolution factors available VRAM, not installed VRAM.
- Worker disappears mid-job: Job status transitions to `Suspended`. Checkpoint in `.koan/jobs/{id}/output/checkpoints/` preserves progress. When worker reconnects, job resumes from last checkpoint. Caller receives `JobSuspended` progress event.
- Mixed accelerator fleet (CUDA + ROCm + Metal): `Accelerator.Any` resolves to best available by VRAM. Explicit `Accelerator.ROCm` filters to ROCm-only nodes. Resolution reports alternatives from other accelerator types.
- Accelerator runtime not installed (e.g., CUDA GPU present but no CUDA toolkit): Resource reported with `Accelerator.CUDA` but empty `Runtimes` array. Resolution skips node with reason `"CUDA device detected but no compatible runtime installed"`.
- `Compute.Check()` in air-gapped environment: Verifies models present in local catalog (AI-0023), runtimes installed, VRAM sufficient — no network calls required.

## Context

Koan.AI routes inference through the source-member architecture (AI-0015): sources aggregate members (endpoints), the router elects a source by priority, then selects a member by policy (Fallback, RoundRobin). This works well for inference — the workload is short-lived, latency-sensitive, and any member with the right model can serve it.

Training, conversion, quantization, and evaluation are fundamentally different workloads:

1. **Long-running.** A LoRA fine-tune takes minutes to hours. A full fine-tune takes hours to days. These are jobs, not requests.
2. **Hardware-specific.** Training requires specific accelerator support (CUDA for PyTorch, ROCm for AMD GPUs, Metal for Apple Silicon). Inference can often fall back to CPU; training at scale cannot.
3. **VRAM-constrained.** An 8B parameter model with QLoRA needs ~18 GB VRAM. A developer laptop with 8 GB cannot run it. A network A100 with 80 GB can.
4. **Runtime-dependent.** Training needs Python + PyTorch. Conversion needs llama.cpp or optimum. ONNX inference needs ONNX Runtime. These runtimes may exist on different machines.

The source-member architecture does not model any of this. It routes by model availability and endpoint health. It has no concept of VRAM, accelerator type, runtime availability, or job lifecycle.

ZenGarden already solves topology discovery. Stones advertise offerings (MongoDB, Redis, Ollama) with capabilities. The infrastructure for discovering what exists on the network is proven. What is missing is **compute capability advertising** — stones declaring their GPU hardware, available VRAM, installed runtimes, and current utilization.

### Non-CUDA GPU Support

The GPU ecosystem is no longer NVIDIA-only. AMD's ROCm stack supports PyTorch training on Linux. Apple Silicon's Metal Performance Shaders power local inference and fine-tuning on macOS. Intel's OneAPI targets data-center workloads. Microsoft's DirectML provides a universal GPU abstraction on Windows.

Koan must abstract this heterogeneity. A developer should write `Training.Train(options)` and have the framework resolve which accelerator to use, which runtime supports that accelerator, and where the work should execute. The `Accelerator` enum is the abstraction boundary — no vendor name appears in the public verb API.

### Air-Gapped Operation

Not every deployment has ZenGarden. Development laptops, CI pipelines, and air-gapped environments operate without network topology. The compute fabric must degrade gracefully:

- **No ZenGarden:** Discovery returns local resources only. Resolution considers only local compute. All facade verbs work.
- **No GPU:** Discovery returns CPU-only resource. Resolution warns about performance. Training and conversion proceed on CPU where runtimes support it.
- **No runtimes:** Discovery returns hardware info but empty runtime list. `Compute.Check()` reports exactly what is missing and how to install it.

The design principle: **Compute.* is always available.** ZenGarden adds network resources to the pool; it is not a prerequisite.

## Decision

### Part 1: Shared Boundary Models

These models are defined in `Koan.AI.Contracts.Shared` and referenced by Compute, Training (AI-0028), Model (AI-0023), and Eval (AI-0029).

```csharp
// ── Accelerator abstraction ──

public enum Accelerator
{
    None,       // CPU only — no accelerator available or requested
    Any,        // Framework selects best available accelerator
    CUDA,       // NVIDIA GPU (Linux, Windows)
    ROCm,       // AMD GPU (Linux; Windows uses DirectML)
    Metal,      // Apple Silicon (macOS)
    DirectML,   // Windows universal GPU (AMD, Intel, NVIDIA fallback)
    OneAPI      // Intel GPU / accelerator
}

// ── Compute location ──

public enum ComputeLocation
{
    Local,      // Same machine as the caller
    Network,    // Reachable via ZenGarden topology
    Cloud       // Future: managed cloud compute
}

// ── Compute status ──

public enum ComputeStatus
{
    Available,  // Ready to accept work
    Busy,       // Running a job (may still have capacity)
    Offline     // Unreachable or shut down
}

// ── Compute requirement (intent, not destination) ──

public sealed record ComputeRequirement(
    Accelerator Accelerator = Accelerator.Any,
    long? MinVramBytes = null,
    ComputeLocation? Location = null,
    string? PreferredNode = null);

// ── Job lifecycle (shared with Training, Model.Convert, Eval) ──

public sealed record JobRef(string Id, JobStatus Status);

public enum JobStatus
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled,
    Suspended   // Worker disconnected; checkpoint preserved
}
```

### Part 2: ComputeResource Model

Each discoverable compute unit — local GPU, network GPU, CPU fallback — is represented as a `ComputeResource`. Multiple GPUs on a single machine produce multiple resources.

```csharp
public sealed record ComputeResource(
    string Id,
    Accelerator Accelerator,
    long VramBytes,
    long AvailableVramBytes,
    string? DeviceName,
    ComputeLocation Location,
    IReadOnlyList<string> Runtimes,
    string? StoneId,
    ComputeStatus Status,
    double? GpuUtilization = null,
    IReadOnlyList<string>? LoadedModels = null)
{
    /// <summary>
    /// Whether this resource can satisfy a compute requirement.
    /// </summary>
    public bool Satisfies(ComputeRequirement requirement)
    {
        if (Status == ComputeStatus.Offline)
            return false;

        if (requirement.Accelerator != Accelerator.Any
            && requirement.Accelerator != Accelerator.None
            && requirement.Accelerator != Accelerator)
            return false;

        if (requirement.MinVramBytes is { } minVram && AvailableVramBytes < minVram)
            return false;

        if (requirement.Location is { } loc && loc != Location)
            return false;

        if (requirement.PreferredNode is { } node && StoneId != node && Id != node)
            return false;

        return true;
    }
}
```

### Part 3: ComputeResolution

Resolution is **advisory by default**. The caller asks "where should this workload run?" and receives a recommendation with reasoning — not automatic dispatch.

```csharp
public sealed record ComputeResolution(
    ComputeResource Target,
    string Reason,
    IReadOnlyList<ComputeResource> Alternatives,
    ComputeResource? LocalFallback,
    IReadOnlyList<string> Warnings)
{
    /// <summary>
    /// Whether the target is on this machine. If false, work delegation is required.
    /// </summary>
    public bool IsLocal => Target.Location == ComputeLocation.Local;

    /// <summary>
    /// Whether warnings exist (e.g., CPU fallback, low VRAM, no accelerator).
    /// </summary>
    public bool HasWarnings => Warnings.Count > 0;
}
```

### Part 4: The `Compute.*` Facade

All verbs are static methods on the `Compute` class, consistent with Koan's entity-first static API style (`Entity.Get()`, `Model.Pull()`, `Client.Chat()`).

#### 4.1 Discovery Verbs

```csharp
public static class Compute
{
    // ── Discovery ──

    /// <summary>
    /// All discoverable compute resources: local hardware + ZenGarden network.
    /// Air-gapped: returns local resources only.
    /// </summary>
    public static ValueTask<IReadOnlyList<ComputeResource>> Available(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rich fleet view with utilization, loaded models, and health.
    /// Sam's primary entry point for infrastructure monitoring.
    /// </summary>
    public static ValueTask<ComputeFleet> Fleet(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Capacity planning: usage trends, projected saturation, recommendations.
    /// </summary>
    public static ValueTask<ComputeForecast> Forecast(
        TimeSpan period,
        CancellationToken cancellationToken = default);
}
```

**`ComputeFleet` model:**

```csharp
public sealed record ComputeFleet(
    IReadOnlyList<ComputeResource> Resources,
    long TotalVramBytes,
    long AvailableVramBytes,
    int ActiveJobs,
    DateTimeOffset DiscoveredAt)
{
    /// <summary>
    /// Resources filtered by accelerator type.
    /// </summary>
    public IReadOnlyList<ComputeResource> ByAccelerator(Accelerator accelerator)
        => Resources.Where(r => r.Accelerator == accelerator).ToList();

    /// <summary>
    /// Resources filtered by runtime availability.
    /// </summary>
    public IReadOnlyList<ComputeResource> WithRuntime(string runtime)
        => Resources.Where(r => r.Runtimes.Contains(runtime)).ToList();
}
```

#### 4.2 Resolution Verbs

```csharp
public static class Compute
{
    // ── Resolution ──

    /// <summary>
    /// Find the best resource for a workload. Returns advisory result.
    /// Resolution rules:
    ///   1. Can run locally with sufficient VRAM? → Local (fastest, no network).
    ///   2. Insufficient local VRAM? → Network resource with enough VRAM.
    ///   3. Missing runtime? → Resource with the runtime installed.
    ///   4. No accelerator anywhere? → CPU fallback with warning.
    ///   5. Too large for CPU? → Reject with capacity diagnostic.
    /// </summary>
    public static ValueTask<ComputeResolution> Resolve(
        ComputeWorkload workload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a hard compute requirement. Work will not execute unless satisfied.
    /// </summary>
    public static ComputeRequirement Require(
        long? minVram = null,
        Accelerator? accelerator = null,
        ComputeLocation? location = null,
        string? node = null);

    /// <summary>
    /// Create a soft preference. Work may fall back to lesser resources with a warning.
    /// </summary>
    public static ComputePreference Prefer(
        long? minVram = null,
        Accelerator? accelerator = null,
        ComputeLocation? location = null,
        string? node = null);

    /// <summary>
    /// Readiness check: verify that models, runtimes, and capabilities
    /// are available before submitting work. Designed for air-gapped
    /// pre-flight verification.
    /// </summary>
    public static ValueTask<ComputeReadiness> Check(
        ComputeReadinessSpec spec,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Auto-routing sentinel. When passed as compute parameter to Training.Train()
    /// or Model.Convert(), enables automatic dispatch without manual resolution.
    /// </summary>
    public static readonly ComputeRequirement Auto = new(Accelerator.Any);
}
```

**`ComputeWorkload` describes what the caller needs:**

```csharp
public sealed record ComputeWorkload(
    string Kind,                            // "training", "conversion", "evaluation", "inference"
    long EstimatedVramBytes,
    Accelerator RequiredAccelerator = Accelerator.Any,
    IReadOnlyList<string>? RequiredRuntimes = null,
    ComputeLocation? PreferredLocation = null,
    TimeSpan? EstimatedDuration = null);
```

**`ComputePreference` allows soft degradation:**

```csharp
public sealed record ComputePreference(
    long? MinVramBytes = null,
    Accelerator? Accelerator = null,
    ComputeLocation? Location = null,
    string? Node = null)
{
    /// <summary>
    /// Convert to a hard requirement (no fallback).
    /// </summary>
    public ComputeRequirement AsRequired() => new(
        Accelerator ?? Contracts.Shared.Accelerator.Any,
        MinVramBytes,
        Location,
        Node);
}
```

**`ComputeReadinessSpec` and `ComputeReadiness` for pre-flight checks:**

```csharp
public sealed record ComputeReadinessSpec(
    IReadOnlyList<string>? RequiredModels = null,
    IReadOnlyList<string>? RequiredRuntimes = null,
    long? MinVramBytes = null,
    Accelerator? RequiredAccelerator = null);

public sealed record ComputeReadiness(
    bool Ready,
    IReadOnlyList<ComputeReadinessIssue> Issues);

public sealed record ComputeReadinessIssue(
    string Category,    // "model", "runtime", "vram", "accelerator"
    string Description,
    string? Guidance);  // Installation/resolution guidance
```

#### 4.3 Management Verbs

```csharp
public static class Compute
{
    // ── Management ──

    /// <summary>
    /// Update a runtime on a remote node. Requires Koan.AI.Worker on the target.
    /// </summary>
    public static ValueTask<JobRef> Update(
        string node,
        string runtime,
        string version,
        CancellationToken cancellationToken = default);
}
```

### Part 5: Local Compute Discovery

Local discovery runs at startup and caches results. Re-discovery is triggered by `Compute.Available(forceRefresh: true)` or when a new runtime is detected.

#### 5.1 Platform-Specific GPU Detection

```csharp
internal interface ILocalComputeDetector
{
    ValueTask<IReadOnlyList<ComputeResource>> Detect(CancellationToken cancellationToken);
}
```

| Platform | Implementation | Detection Method |
|----------|---------------|-----------------|
| Windows | `WindowsComputeDetector` | WMI `Win32_VideoController` for GPU info. Registry for driver version. `dxdiag` fallback for VRAM. DirectML availability via DLL probe. |
| Linux | `LinuxComputeDetector` | `/sys/class/drm/card*/device/` for GPU enumeration. `nvidia-smi` for NVIDIA VRAM/utilization. `/sys/class/drm/card*/device/mem_info_vram_total` for AMD. |
| macOS | `MacOSComputeDetector` | `system_profiler SPDisplaysDataType -json` for GPU info. `sysctl hw.memsize` for unified memory (Metal). IOKit for detailed GPU properties. |

#### 5.2 Accelerator Inference Rules

The detector determines the `Accelerator` enum value from hardware, not from runtime:

| GPU Vendor | OS | Accelerator | Notes |
|-----------|-----|-------------|-------|
| NVIDIA | Any | `CUDA` | Requires CUDA toolkit for training runtimes |
| AMD | Linux | `ROCm` | Requires ROCm stack; supported GPUs: RX 7000+, MI series |
| AMD | Windows | `DirectML` | ROCm not supported on Windows; DirectML provides GPU access |
| Apple Silicon | macOS | `Metal` | Unified memory reported as VRAM |
| Intel Arc/Xe | Linux | `OneAPI` | Requires oneAPI toolkit |
| Intel Arc/Xe | Windows | `DirectML` | OneAPI Windows support limited; DirectML preferred |
| Integrated / None | Any | `None` | CPU-only resource still created |

#### 5.3 Runtime Detection

After GPU detection, the detector probes for installed runtimes:

```csharp
internal interface IRuntimeProbe
{
    string RuntimeName { get; }               // "ollama", "onnxruntime", "python", "docker"
    ValueTask<bool> IsAvailable(CancellationToken ct);
    ValueTask<string?> GetVersion(CancellationToken ct);
}
```

| Runtime | Probe Method |
|---------|-------------|
| Ollama | HTTP `GET http://localhost:11434/api/version` |
| ONNX Runtime | DLL probe for `onnxruntime.dll` / `libonnxruntime.so` |
| Python + PyTorch | `python -c "import torch; print(torch.__version__)"` |
| Docker / Podman | `docker info --format '{{.ServerVersion}}'` or `podman info` |
| TorchServe | HTTP `GET http://localhost:8080/ping` |
| TGI | HTTP `GET http://localhost:8081/health` |
| TEI | HTTP `GET http://localhost:8082/health` |

Runtime probe results are cached for 5 minutes. Probes that fail (command not found, connection refused) are marked unavailable without error — the absence of a runtime is informational, not an error.

### Part 6: Network Discovery via ZenGarden

ZenGarden stones advertise compute capabilities as part of their topology registration. This extends the existing capability model without breaking the topology API contract.

#### 6.1 Compute Capability Advertisement

Stones running `Koan.AI.Worker` include a `compute` section in their capability advertisement:

```json
{
  "stone_id": "zen-gpu-01",
  "stone_name": "gpu-server",
  "endpoint": "http://192.168.1.172:7185",
  "services": ["koan-ai-worker"],
  "capabilities": {
    "compute": {
      "devices": [
        {
          "id": "gpu-0",
          "accelerator": "cuda",
          "device_name": "NVIDIA A100 80GB PCIe",
          "vram_bytes": 85899345920,
          "available_vram_bytes": 68719476736,
          "gpu_utilization": 0.12,
          "loaded_models": ["llama3.1:70b"]
        },
        {
          "id": "gpu-1",
          "accelerator": "cuda",
          "device_name": "NVIDIA A100 80GB PCIe",
          "vram_bytes": 85899345920,
          "available_vram_bytes": 85899345920,
          "gpu_utilization": 0.0,
          "loaded_models": []
        }
      ],
      "runtimes": ["ollama", "tgi", "tei", "python", "torchserve"],
      "status": "available",
      "active_jobs": 1
    }
  }
}
```

#### 6.2 Topology Hydration

When `Compute.Available()` is called, the compute fabric:

1. Returns cached local resources immediately.
2. Queries ZenGarden topology for stones with `compute` capabilities (uses existing `GET /api/v1/garden/topology` — see ZenGarden memory for envelope pattern).
3. Deserializes `TopologyApiResponse` envelope → extracts `compute` capability sections.
4. Converts each device entry to a `ComputeResource` with `Location = ComputeLocation.Network`.
5. Merges local + network resources into a unified list.
6. Caches network resources with 30-second TTL. Heartbeat refreshes (every 5 minutes, per ZenGarden protocol) update utilization and availability.

```csharp
// Internal: topology hydration
internal sealed class ZenGardenComputeDiscovery : INetworkComputeDiscovery
{
    private readonly IZenGardenClient _garden;

    public async ValueTask<IReadOnlyList<ComputeResource>> Discover(
        CancellationToken cancellationToken)
    {
        var topology = await _garden.GetTopologyAsync(cancellationToken);

        return topology
            .Where(stone => stone.Capabilities?.ContainsKey("compute") == true)
            .SelectMany(stone => MapStoneToResources(stone))
            .ToList();
    }

    private static IEnumerable<ComputeResource> MapStoneToResources(
        TopologyEntry stone)
    {
        var compute = stone.Capabilities!["compute"];

        foreach (var device in compute.Devices)
        {
            yield return new ComputeResource(
                Id: $"{stone.StoneId}/{device.Id}",
                Accelerator: ParseAccelerator(device.Accelerator),
                VramBytes: device.VramBytes,
                AvailableVramBytes: device.AvailableVramBytes,
                DeviceName: device.DeviceName,
                Location: ComputeLocation.Network,
                Runtimes: compute.Runtimes,
                StoneId: stone.StoneId,
                Status: ParseStatus(compute.Status),
                GpuUtilization: device.GpuUtilization,
                LoadedModels: device.LoadedModels);
        }
    }
}
```

#### 6.3 Air-Gapped Fallback

When ZenGarden is not configured or unreachable:

```csharp
internal sealed class NullNetworkComputeDiscovery : INetworkComputeDiscovery
{
    public ValueTask<IReadOnlyList<ComputeResource>> Discover(
        CancellationToken cancellationToken)
        => ValueTask.FromResult<IReadOnlyList<ComputeResource>>(Array.Empty<ComputeResource>());
}
```

The DI registration selects the implementation based on whether `IZenGardenClient` is registered:

```csharp
// In KoanAutoRegistrar for Koan.AI.Compute
if (services.Any(s => s.ServiceType == typeof(IZenGardenClient)))
    services.AddSingleton<INetworkComputeDiscovery, ZenGardenComputeDiscovery>();
else
    services.AddSingleton<INetworkComputeDiscovery, NullNetworkComputeDiscovery>();
```

The boot report reflects the mode:

```
── Koan.AI.Compute ──────────────────────────────────
  Local Compute
    GPU 0 .............. NVIDIA RTX 4060 Ti (8 GB, CUDA)
    Runtimes ........... ollama 0.9.1, python 3.12, docker 27.5
  Network Compute
    Mode ............... ZenGarden (3 stones, 4 GPUs, 320 GB total)
    gpu-server/gpu-0 ... NVIDIA A100 80GB (CUDA, 68 GB free)
    gpu-server/gpu-1 ... NVIDIA A100 80GB (CUDA, 80 GB free)
    ml-node/gpu-0 ...... AMD MI300X (ROCm, 192 GB free)
    dev-mac/gpu-0 ...... Apple M4 Max (Metal, 128 GB unified)
```

Or in air-gapped mode:

```
── Koan.AI.Compute ──────────────────────────────────
  Local Compute
    GPU 0 .............. NVIDIA RTX 4060 Ti (8 GB, CUDA)
    Runtimes ........... ollama 0.9.1
  Network Compute
    Mode ............... Local Only (ZenGarden not configured)
```

### Part 7: Compute Resolution Rules

Resolution follows a deterministic priority chain. The resolver receives a `ComputeWorkload` and returns a `ComputeResolution`.

```
┌──────────────────────────────────────────────────────┐
│                   Resolve(workload)                   │
├──────────────────────────────────────────────────────┤
│                                                      │
│  1. Collect all resources (local + network)          │
│  2. Filter by required accelerator                   │
│  3. Filter by required runtimes                      │
│  4. Sort by preference:                              │
│     a. PreferredNode match (if specified)            │
│     b. Local over network (minimize latency)         │
│     c. Available VRAM descending                     │
│     d. Lower utilization preferred                   │
│  5. Select first with sufficient VRAM                │
│                                                      │
│  Fallback chain:                                     │
│  ├─ Sufficient match found? → Return as Target       │
│  ├─ No VRAM match? → Find network resource           │
│  ├─ No runtime match? → Find resource with runtime   │
│  ├─ No accelerator? → CPU fallback + warning         │
│  └─ Too large for CPU? → Reject + diagnostic         │
│                                                      │
└──────────────────────────────────────────────────────┘
```

```csharp
internal sealed class ComputeResolver : IComputeResolver
{
    public async ValueTask<ComputeResolution> Resolve(
        ComputeWorkload workload,
        IReadOnlyList<ComputeResource> resources,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();

        // Filter by accelerator
        var candidates = workload.RequiredAccelerator == Accelerator.Any
            ? resources.Where(r => r.Status != ComputeStatus.Offline).ToList()
            : resources.Where(r =>
                r.Status != ComputeStatus.Offline
                && r.Accelerator == workload.RequiredAccelerator).ToList();

        if (candidates.Count == 0 && workload.RequiredAccelerator != Accelerator.Any)
        {
            // Fall back to any accelerator with warning
            candidates = resources
                .Where(r => r.Status != ComputeStatus.Offline)
                .ToList();
            warnings.Add(
                $"No {workload.RequiredAccelerator} resource found. " +
                $"Available: {string.Join(", ", resources.Select(r => r.Accelerator).Distinct())}");
        }

        // Filter by required runtimes
        if (workload.RequiredRuntimes is { Count: > 0 } runtimes)
        {
            var withRuntimes = candidates
                .Where(r => runtimes.All(rt => r.Runtimes.Contains(rt)))
                .ToList();

            if (withRuntimes.Count == 0)
            {
                var missing = runtimes
                    .Where(rt => !candidates.Any(r => r.Runtimes.Contains(rt)));
                warnings.Add($"No resource has all required runtimes. Missing: {string.Join(", ", missing)}");
            }
            else
            {
                candidates = withRuntimes;
            }
        }

        // Sort by preference
        var sorted = candidates
            .OrderByDescending(r => r.Location == ComputeLocation.Local ? 1 : 0)
            .ThenByDescending(r => r.AvailableVramBytes)
            .ThenBy(r => r.GpuUtilization ?? 0.0)
            .ToList();

        // Find best match with sufficient VRAM
        var target = sorted.FirstOrDefault(r => r.AvailableVramBytes >= workload.EstimatedVramBytes);

        if (target is null)
        {
            // CPU fallback
            var cpuResource = sorted.FirstOrDefault(r => r.Accelerator == Accelerator.None)
                ?? new ComputeResource(
                    "cpu-fallback", Accelerator.None, 0, 0, "CPU",
                    ComputeLocation.Local, [], null, ComputeStatus.Available);

            warnings.Add(
                $"No resource has sufficient VRAM ({workload.EstimatedVramBytes / (1024 * 1024 * 1024.0):F1} GB required). " +
                "Falling back to CPU. Expect significantly longer execution times.");

            return new ComputeResolution(
                Target: cpuResource,
                Reason: "CPU fallback — insufficient VRAM across all resources",
                Alternatives: sorted,
                LocalFallback: cpuResource,
                Warnings: warnings);
        }

        var localFallback = sorted.FirstOrDefault(r => r.Location == ComputeLocation.Local);

        return new ComputeResolution(
            Target: target,
            Reason: target.Location == ComputeLocation.Local
                ? $"Local {target.Accelerator} with {target.AvailableVramBytes / (1024 * 1024 * 1024.0):F0} GB available"
                : $"Network {target.Accelerator} on {target.StoneId} with {target.AvailableVramBytes / (1024 * 1024 * 1024.0):F0} GB available",
            Alternatives: sorted.Where(r => r != target).ToList(),
            LocalFallback: localFallback,
            Warnings: warnings);
    }
}
```

### Part 8: Koan.AI.Worker — Compute-Capable Adapter

The Worker is a lightweight service that runs on machines with GPU hardware. It registers as an **AI adapter** with capabilities reflecting the remote machine's hardware and installed runtimes, advertises compute via ZenGarden, and accepts delegated work.

#### 8.1 Worker as Adapter

`Koan.AI.Worker` is itself an `IAiAdapter`. It registers with capabilities that reflect the remote machine's actual hardware and software. For example, a Worker on a machine with an A100 and Ollama installed might declare:

```csharp
// Worker adapter registration — capabilities reflect the machine
public IReadOnlySet<string> Capabilities => new HashSet<string>
{
    AiCapability.Train,           // PyTorch available
    AiCapability.Align,           // trl available
    AiCapability.Convert,         // llama.cpp available
    AiCapability.Quantize,        // llama.cpp available
    AiCapability.Serve.GGUF,      // Ollama installed
    AiCapability.Chat,            // Inference via Ollama
    AiCapability.Embed,           // Embedding via Ollama
    AiCapability.MetricCompute,   // Python eval libraries installed
};
```

Callers resolve compute-bound operations through the standard adapter resolution pattern. For example, `Training.Train()` internally calls `AdapterResolver.Resolve(registry, AiCapability.Train)` to find the adapter (local or remote Worker) that can handle training. If only the remote Worker has `Train` capability, the work is transparently delegated.

#### 8.2 Registration

```csharp
// Program.cs on a GPU-capable machine
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddKoan()
    .AsAiWorker();     // Registers as adapter with hardware-inferred capabilities

var app = builder.Build();
app.Run();
```

`AsAiWorker()` does the following:

1. Runs local compute detection (Part 5).
2. Infers adapter capabilities from detected hardware and runtimes (e.g., `Train` if PyTorch is available, `Serve.GGUF` if Ollama is installed).
3. Registers the Worker as an `IAiAdapter` in the adapter registry with those capabilities.
4. Registers with ZenGarden as a stone with `compute` capability (Part 6.1).
5. Starts a background service that:
   - Refreshes GPU utilization every 15 seconds.
   - Updates ZenGarden capability advertisement on utilization change > 10%.
   - Listens for work delegation requests via ZenGarden protocol.
6. Exposes management endpoints:
   - `GET /api/v1/compute/status` — current resource state.
   - `POST /api/v1/compute/jobs` — accept a new job.
   - `GET /api/v1/compute/jobs/{id}/progress` — SSE stream of progress events.
   - `DELETE /api/v1/compute/jobs/{id}` — cancel a running job.

#### 8.3 Configuration

```json
{
  "Koan": {
    "Ai": {
      "Compute": {
        "Worker": {
          "Enabled": true,
          "AcceptRemoteJobs": true,
          "MaxConcurrentJobs": 2,
          "JobDirectory": ".koan/jobs",
          "AllowedRuntimes": ["ollama", "python", "onnxruntime"],
          "GpuReservationPercent": 10
        }
      }
    }
  }
}
```

- `GpuReservationPercent`: Percentage of VRAM to keep free (headroom for OS/display). Default 10%.
- `AllowedRuntimes`: Which runtimes this worker will use. Prevents unexpected execution.
- `MaxConcurrentJobs`: Limits concurrent jobs to prevent VRAM contention.

### Part 9: Work Delegation Protocol

When a workload is resolved to a network resource, the framework delegates work using a filesystem-based contract. This contract is the same whether work runs locally or remotely — the Worker service uses the same directory structure.

#### 9.1 Job Directory Structure

```
.koan/jobs/{job-id}/
├── input/
│   ├── recipe.json          ← Full job specification (idempotent, serializable)
│   ├── data/                ← Training data, evaluation sets
│   │   ├── train.jsonl
│   │   └── eval.jsonl
│   └── base-model/          ← Model weights (or ModelRef for catalog resolution)
│       └── model-ref.json   ← {"id": "meta-llama/Llama-3.1-8B-Instruct", "version": 2}
├── output/
│   ├── model/               ← Output weights (training jobs)
│   │   └── adapter/         ← LoRA adapter weights
│   ├── metrics.json         ← Final metrics (loss, accuracy, eval scores)
│   ├── checkpoints/         ← Resumable checkpoints (ordered by step)
│   │   ├── checkpoint-500/
│   │   └── checkpoint-1000/
│   └── artifacts/           ← Conversion outputs, evaluation reports, etc.
├── progress.jsonl           ← Append-only progress log (streamed back to caller)
└── status                   ← Single word: queued, running, completed, failed, suspended
```

#### 9.2 Recipe Format

```json
{
  "job_id": "j-2026-03-20-a1b2c3",
  "kind": "training",
  "created_at": "2026-03-20T14:30:00Z",
  "base_model": {"id": "meta-llama/Llama-3.1-8B-Instruct", "version": null},
  "method": "lora",
  "method_options": {
    "rank": 16,
    "alpha": 32,
    "target_modules": ["q_proj", "v_proj"],
    "learning_rate": 2e-4,
    "epochs": 3,
    "batch_size": 4
  },
  "data": {
    "train": "data/train.jsonl",
    "eval": "data/eval.jsonl",
    "format": "instruction"
  },
  "compute": {
    "accelerator": "any",
    "min_vram_bytes": 19327352832
  },
  "output": {
    "register_model": true,
    "model_name": "acme-support",
    "tags": ["support", "v4-candidate"]
  }
}
```

#### 9.3 Network Delegation Flow

```
Caller Machine                            Worker Machine (gpu-server)
─────────────────                         ──────────────────────────

1. Compute.Resolve(workload)
   → Target: gpu-server/gpu-1

2. Serialize recipe.json
   POST /api/v1/compute/jobs
   Body: recipe + dataset (chunked, resumable)
                                          3. Worker receives job
                                             Creates .koan/jobs/{id}/
                                             Writes recipe.json, data/

                                          4. Worker resolves base model
                                             from Model Catalog (AI-0023)
                                             → avoids transferring 16 GB weights

                                          5. Worker executes job
                                             Writes progress.jsonl
                                             Writes checkpoints/

   ← SSE: progress events ←──────────── 6. Worker streams progress
     {"step": 500, "loss": 0.42,             via GET /jobs/{id}/progress
      "lr": 1.8e-4, "eta_seconds": 1200}

                                          7. Job completes
                                             Writes output/model/adapter/
                                             Writes output/metrics.json
                                             Writes status: "completed"

8. Transfer output artifacts ←──────────  (chunked, resumable)

9. Register in local Model Catalog
   await Model.Register(path, lineage)
```

#### 9.4 Progress Line Format

Each line in `progress.jsonl` is a self-contained JSON object:

```json
{"ts":"2026-03-20T14:31:00Z","step":100,"total_steps":3000,"loss":1.82,"lr":2e-4,"vram_used_gb":17.2}
{"ts":"2026-03-20T14:32:15Z","step":200,"total_steps":3000,"loss":1.14,"lr":1.9e-4,"vram_used_gb":17.3}
{"ts":"2026-03-20T14:33:28Z","step":300,"total_steps":3000,"loss":0.87,"lr":1.8e-4,"vram_used_gb":17.2,"checkpoint":"checkpoint-300"}
```

Callers consume progress via callback:

```csharp
var job = await Training.Train(options,
    progress: p => Console.WriteLine($"Step {p.Step}/{p.TotalSteps} — loss: {p.Loss:F3}"));
```

### Part 10: Accelerator-to-Runtime Mapping

Not all runtimes support all accelerators. The fabric maintains a compatibility matrix used during resolution:

| Runtime | CUDA | ROCm | Metal | DirectML | OneAPI | CPU |
|---------|------|------|-------|----------|--------|-----|
| Ollama | Auto | Auto | Auto | N/A | N/A | Auto |
| ONNX Runtime | CUDAExecutionProvider | ROCMExecutionProvider | CoreMLExecutionProvider | DmlExecutionProvider | N/A | CPUExecutionProvider |
| PyTorch | `torch.cuda` | `torch.cuda` (ROCm) | `torch.mps` | N/A | `torch.xpu` | `torch.cpu` |
| TGI | CUDA only | N/A | N/A | N/A | N/A | Degraded |
| TEI | CUDA only | N/A | N/A | N/A | N/A | Supported |
| TorchServe | CUDA | N/A | N/A | N/A | N/A | Supported |

Notes on non-CUDA support:

- **ROCm + PyTorch**: Uses `torch.cuda` API (AMD's ROCm provides CUDA compatibility layer). Supported on Linux with AMD MI/RX 7000+ GPUs. Training works; some operators may fall back to CPU.
- **Metal + PyTorch**: Uses `torch.mps` backend. Supported on macOS with Apple Silicon. LoRA fine-tuning works; some advanced training techniques have limited operator coverage.
- **DirectML**: Windows-universal GPU access. Inference via ONNX Runtime `DmlExecutionProvider`. Training via DirectML backend in ONNX Runtime Training. Broadest hardware support on Windows (NVIDIA, AMD, Intel).
- **OneAPI + PyTorch**: Uses `torch.xpu` for Intel GPUs. Training support maturing; inference well-supported via ONNX Runtime OpenVINO.

The resolution engine uses this matrix to match workload requirements (e.g., "training" requires PyTorch) to resources with compatible runtimes:

```csharp
internal static class AcceleratorRuntimeMatrix
{
    /// <summary>
    /// Returns runtimes that support the given accelerator for the given workload kind.
    /// </summary>
    public static IReadOnlyList<string> CompatibleRuntimes(
        Accelerator accelerator,
        string workloadKind)
    {
        return (accelerator, workloadKind) switch
        {
            (Accelerator.CUDA, "training") => ["python", "torchserve"],
            (Accelerator.CUDA, "inference") => ["ollama", "tgi", "tei", "onnxruntime", "python", "torchserve"],
            (Accelerator.CUDA, "conversion") => ["python", "docker"],
            (Accelerator.ROCm, "training") => ["python"],
            (Accelerator.ROCm, "inference") => ["ollama", "onnxruntime", "python"],
            (Accelerator.Metal, "training") => ["python"],
            (Accelerator.Metal, "inference") => ["ollama", "onnxruntime", "python"],
            (Accelerator.DirectML, "training") => ["onnxruntime"],
            (Accelerator.DirectML, "inference") => ["onnxruntime"],
            (Accelerator.OneAPI, "training") => ["python"],
            (Accelerator.OneAPI, "inference") => ["onnxruntime", "python"],
            (Accelerator.None, _) => ["python", "onnxruntime", "ollama"],
            (Accelerator.Any, _) => ["ollama", "python", "onnxruntime", "tgi", "tei", "torchserve"],
            _ => []
        };
    }
}
```

### Part 11: Usage Scenarios

#### 11.1 Sam (Platform Engineer) — Fleet Monitoring

```csharp
// See everything
var fleet = await Compute.Fleet();
Console.WriteLine($"Total VRAM: {fleet.TotalVramBytes / GiB(1)} GB");
Console.WriteLine($"Available: {fleet.AvailableVramBytes / GiB(1)} GB");
Console.WriteLine($"Active jobs: {fleet.ActiveJobs}");

foreach (var resource in fleet.Resources)
{
    Console.WriteLine($"  {resource.Id}: {resource.DeviceName} " +
        $"({resource.Accelerator}, {resource.AvailableVramBytes / GiB(1)} GB free, " +
        $"{resource.GpuUtilization:P0} util)");
}

// Capacity planning
var forecast = await Compute.Forecast(TimeSpan.FromDays(30));
// → "At current growth rate, VRAM saturation expected in 18 days.
//    Recommendation: add 1x A100 80GB node or reduce concurrent training jobs."
```

#### 11.2 Jun (Model Specialist) — Deploy with Compute Awareness

```csharp
// Check if the fleet can handle a 70B model
var readiness = await Compute.Check(new ComputeReadinessSpec(
    RequiredModels: ["meta-llama/Llama-3.1-70B-Instruct"],
    MinVramBytes: GiB(48),
    RequiredRuntimes: ["ollama"]));

if (!readiness.Ready)
{
    foreach (var issue in readiness.Issues)
        Console.WriteLine($"  [{issue.Category}] {issue.Description}");
        // → [vram] No single resource has 48 GB available. Largest: gpu-server/gpu-0 (38 GB free)
        // → [model] meta-llama/Llama-3.1-70B-Instruct not found in local catalog. Pull required.
    return;
}

// Deploy model — compute resolution is implicit
await Model.Deploy("meta-llama/Llama-3.1-70B-Instruct");
```

#### 11.3 Riku (AI Scientist) — Training Routed to Network GPU

```csharp
// Riku's laptop: 8 GB NVIDIA RTX 4060 Ti
// Training needs ~18 GB VRAM for 8B QLoRA

var dataset = Dataset.From<SupportTicket>(
    where: t => t.Resolved && t.Rating >= 4,
    input: t => t.Question,
    output: t => t.Resolution);

// Compute resolves transparently
var job = await Training.Train(
    "meta-llama/Llama-3.1-8B-Instruct",
    dataset,
    method: TrainMethod.LoRA,
    progress: p => Console.WriteLine($"Step {p.Step}/{p.TotalSteps} loss={p.Loss:F3}"));

// Koan internally:
// 1. Compute.Resolve() → laptop has 8 GB, need 18 GB → route to gpu-server/gpu-1 (80 GB)
// 2. Serialize recipe.json with dataset
// 3. Transfer to gpu-server via Worker protocol
// 4. gpu-server resolves base model from catalog (avoids 16 GB transfer)
// 5. Stream progress back via SSE
// 6. Transfer adapter weights back (~100 MB)
// 7. Register in local Model Catalog

Console.WriteLine($"Job {job.Id}: {job.Status}");
// → Job j-2026-03-20-a1b2c3: Completed
```

#### 11.4 Air-Gapped Development

```csharp
// No ZenGarden, no network. macOS with M4 Max (128 GB unified memory).

var resources = await Compute.Available();
// → [{Id: "local/gpu-0", Accelerator: Metal, VRAM: 128 GB, Runtimes: [ollama, python]}]

// Training runs locally — Metal + PyTorch via torch.mps
var job = await Training.Train(
    "meta-llama/Llama-3.1-8B-Instruct",
    dataset,
    method: TrainMethod.LoRA);
// → Runs on local Metal GPU. No network required.

// Pre-flight check for a specific workload
var readiness = await Compute.Check(new ComputeReadinessSpec(
    RequiredModels: ["meta-llama/Llama-3.1-8B-Instruct"],
    RequiredRuntimes: ["python"],
    RequiredAccelerator: Accelerator.Metal));

if (readiness.Ready)
    Console.WriteLine("All requirements met for local training.");
```

#### 11.5 Explicit Compute Targeting

```csharp
// Hard requirement: must run on CUDA with at least 48 GB
await Training.Train(options,
    compute: Compute.Require(minVram: GiB(48), accelerator: Accelerator.CUDA));

// Soft preference: prefer network GPU, but fall back to local if unavailable
await Training.Train(options,
    compute: Compute.Prefer(location: ComputeLocation.Network));

// Pin to a specific node
await Training.Train(options,
    compute: Compute.Require(node: "gpu-server"));

// Auto-routing: let the fabric decide everything
await Training.Train(options, compute: Compute.Auto);
```

### Part 12: Package Structure

```
Koan.AI.Compute                    ← Compute.* facade, discovery, resolution
├── ComputeFacade.cs               ← Static Compute class
├── Discovery/
│   ├── ILocalComputeDetector.cs
│   ├── WindowsComputeDetector.cs
│   ├── LinuxComputeDetector.cs
│   ├── MacOSComputeDetector.cs
│   ├── IRuntimeProbe.cs
│   ├── OllamaRuntimeProbe.cs
│   ├── PythonRuntimeProbe.cs
│   ├── OnnxRuntimeProbe.cs
│   ├── DockerRuntimeProbe.cs
│   └── ComputeDiscoveryService.cs  ← Orchestrates local + network discovery
├── Resolution/
│   ├── IComputeResolver.cs
│   ├── ComputeResolver.cs
│   └── AcceleratorRuntimeMatrix.cs
├── Network/
│   ├── INetworkComputeDiscovery.cs
│   ├── ZenGardenComputeDiscovery.cs
│   └── NullNetworkComputeDiscovery.cs
├── Models/
│   ├── ComputeResource.cs
│   ├── ComputeResolution.cs
│   ├── ComputeFleet.cs
│   ├── ComputeForecast.cs
│   ├── ComputeWorkload.cs
│   ├── ComputePreference.cs
│   ├── ComputeReadiness.cs
│   └── ComputeReadinessSpec.cs
├── Initialization/
│   └── KoanAutoRegistrar.cs        ← Boot report, DI registration
└── Extensions/
    └── ServiceCollectionExtensions.cs

Koan.AI.Compute.Worker             ← Hosted service for GPU machines
├── WorkerHostedService.cs         ← Background service (registration, heartbeat)
├── JobExecutor.cs                 ← Executes jobs from .koan/jobs/
├── ArtifactTransfer.cs            ← Chunked, resumable file transfer
├── Controllers/
│   ├── ComputeStatusController.cs
│   └── JobController.cs
├── Initialization/
│   └── KoanAutoRegistrar.cs
└── Extensions/
    └── ServiceCollectionExtensions.cs  ← .AsAiWorker()
```

### Part 13: DI Registration and Boot Report

The `Koan.AI.Compute` package follows the "Reference = Intent" principle. Adding the NuGet reference enables compute discovery automatically.

```csharp
// KoanAutoRegistrar in Koan.AI.Compute
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        // Platform-specific detector (compile-time or runtime selection)
        services.AddSingleton<ILocalComputeDetector>(sp =>
        {
            if (OperatingSystem.IsWindows()) return new WindowsComputeDetector();
            if (OperatingSystem.IsLinux()) return new LinuxComputeDetector();
            if (OperatingSystem.IsMacOS()) return new MacOSComputeDetector();
            return new NullComputeDetector();
        });

        // Network discovery: ZenGarden if available, null otherwise
        if (services.Any(s => s.ServiceType == typeof(IZenGardenClient)))
            services.AddSingleton<INetworkComputeDiscovery, ZenGardenComputeDiscovery>();
        else
            services.AddSingleton<INetworkComputeDiscovery, NullNetworkComputeDiscovery>();

        services.AddSingleton<IComputeResolver, ComputeResolver>();
        services.AddSingleton<ComputeDiscoveryService>();

        // Options
        services.AddKoanOptions<ComputeOptions>(configuration, "Koan:Ai:Compute");
    }

    public async ValueTask<BootModuleReport> Describe(/* ... */)
    {
        var module = new BootModuleReport("Koan.AI.Compute");

        var detector = sp.GetRequiredService<ILocalComputeDetector>();
        var localResources = await detector.Detect(CancellationToken.None);

        foreach (var resource in localResources)
        {
            module.AddSetting(
                $"GPU {resource.Id}",
                $"{resource.DeviceName} ({resource.VramBytes / GiB(1)} GB, {resource.Accelerator})",
                BootSettingSource.Auto);
        }

        var networkDiscovery = sp.GetRequiredService<INetworkComputeDiscovery>();
        var networkResources = await networkDiscovery.Discover(CancellationToken.None);

        module.AddSetting("Network Mode",
            networkResources.Count > 0
                ? $"ZenGarden ({networkResources.Count} GPUs discovered)"
                : "Local Only",
            BootSettingSource.Auto);

        return module;
    }
}
```

### Part 14: Integration with AI-0015 Source-Member Architecture and Adapter Resolution

The compute fabric complements, not replaces, the source-member architecture:

| Concern | Source-Member (AI-0015) | Compute Fabric (AI-0024) |
|---------|------------------------|--------------------------|
| Routes | Inference requests | Training, conversion, evaluation jobs |
| Selects by | Model availability, endpoint health | VRAM, accelerator, runtime, utilization |
| Duration | Milliseconds (request/response) | Minutes to hours (async jobs) |
| Protocol | HTTP request → adapter → response | Job submission → progress stream → artifact transfer |
| Lifecycle | Stateless | Stateful (queued → running → completed) |

When both are present, they coordinate:

- `Client.Chat()` continues to use source-member routing (AI-0015).
- `Training.Train()` resolves via `AdapterResolver.Resolve(registry, AiCapability.Train)` — the `Train`-capable adapter (local Python sidecar or remote Worker) handles execution.
- `Model.Convert()` resolves via `AdapterResolver.Resolve(registry, AiCapability.Convert)` — no separate conversion runtime interface.
- `Model.Deploy()` uses compute fabric to find a node, then registers a new source-member endpoint for inference routing.
- `Compute.Fleet()` reports both inference endpoints (from source registry) and compute resources (from compute fabric) for a unified fleet view.

**Capability-driven resolution unifies compute routing.** An adapter declaring `Train` signals it has access to training-capable compute. An adapter declaring `Convert` + `Quantize` signals it can handle model format operations. The `AdapterResolver` finds the right adapter; the compute fabric provides the hardware context (VRAM, accelerator type, utilization) that informs which adapter is best positioned to execute.

```csharp
// Training resolves through adapter capabilities, not separate service interfaces
var trainAdapter = AdapterResolver.Resolve(registry, AiCapability.Train);
// → Returns Worker adapter on gpu-server (has PyTorch + A100)
// → Or local Python sidecar adapter (if local GPU is sufficient)

// Conversion follows the same pattern
var convertAdapter = AdapterResolver.Resolve(registry, AiCapability.Convert);
// → Returns adapter with llama.cpp available
```

When multiple adapters declare the same capability (e.g., two Workers both have `Train`), the compute fabric's resolution rules (VRAM, utilization, locality) disambiguate. The `to:` parameter on `AdapterResolver.Resolve()` can also target a specific adapter explicitly.

### Part 15: Security Considerations

- **Job isolation:** Each job runs in its own directory. No job can read another job's directory. Worker validates job IDs against path traversal.
- **Runtime sandboxing:** Python scripts execute in subprocess with restricted permissions. Docker-based runtimes provide container isolation.
- **Transfer authentication:** Worker endpoints require ZenGarden stone authentication. Unauthorized nodes cannot submit jobs.
- **No secret forwarding:** API keys and credentials are never included in recipe.json. Workers use their own configured credentials for model catalog access.
- **VRAM information disclosure:** GPU utilization and loaded models are visible to all authenticated stones in the topology. This is intentional for routing but should be considered in multi-tenant scenarios.

## Consequences

### Positive

- **Hardware-agnostic compute** — `Accelerator` enum abstracts NVIDIA, AMD, Apple, Intel, and Microsoft GPU stacks behind a single type.
- **Network-transparent routing** — A laptop developer transparently delegates to a network A100 without writing deployment code.
- **Air-gapped capable** — Full functionality without ZenGarden; network compute is additive, not required.
- **ZenGarden-native** — Extends proven topology discovery rather than inventing a new discovery protocol.
- **Advisory by default** — Resolution recommends; `Compute.Auto` opts in to automatic dispatch. Developers stay in control.
- **Filesystem contract for jobs** — Inspectable, debuggable, resumable. No opaque binary protocols.
- **Non-CUDA GPUs are first-class** — ROCm, Metal, DirectML, OneAPI all have explicit representation and runtime mapping.
- **Progressive disclosure** — `Compute.Available()` (one-liner) to `Compute.Resolve(workload)` (rich) to `Compute.Require(...)` (explicit) to Worker configuration (full control).

### Negative / Trade-offs

- **Local detection is platform-specific.** Three detector implementations (Windows, Linux, macOS) must be maintained. GPU detection APIs differ significantly across platforms.
- **VRAM estimation is approximate.** Actual VRAM usage depends on batch size, sequence length, quantization, and framework version. `EstimatedVramBytes` in `ComputeWorkload` is a guideline, not a guarantee.
- **Network delegation adds latency.** Job submission, dataset transfer, and artifact retrieval add overhead compared to local execution. For small jobs (< 1 minute), local execution may be faster even with a slower GPU.
- **Worker is a separate deployment.** Organizations must deploy and maintain `Koan.AI.Worker` on GPU machines. This is operational overhead, mitigated by the minimal `AsAiWorker()` setup.
- **Checkpoint format dependency.** Resumability depends on the training framework writing compatible checkpoints. Custom training scripts (Level 3/4 in AI-0028) must follow checkpoint conventions for resume support.
- **ROCm and Metal training are less mature.** Some PyTorch operators are not yet supported on ROCm and Metal backends. The framework cannot predict all operator compatibility failures ahead of time — some jobs will fail at runtime with accelerator-specific errors.

## References

- AI-0015: Canonical Source-Member Architecture — inference routing model that Compute Fabric complements for non-inference workloads
- AI-0022: Unified AI Lifecycle Vision — vision document defining `Compute.*` as Part 5 of the eight bounded contexts
- AI-0023: Model Catalog — `ModelRef` resolution on workers (avoids transferring model weights); dependency for this ADR
- AI-0028: Training and Dataset — primary consumer of compute resolution for training job placement
- AI-0029: Eval — uses compute resolution for evaluation workload placement
- ZenGarden topology: `src/Koan.ZenGarden/ZenGardenClient.cs` — topology API (`GET /api/v1/garden/topology`), capability advertising, stone registration
- ZenGarden capability surface: `src/Koan.ZenGarden/ZenGardenCapabilitySurface.cs` — capability wish/subscribe pattern
- `Koan.Core.Orchestration.ConnectionStringParser` — existing connection string parsing for compute endpoint resolution
- `Koan.Core.Modules.OptionsExtensions` — `AddKoanOptions<T>()` for compute configuration binding
