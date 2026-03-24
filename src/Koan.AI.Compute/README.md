# Koan.AI.Compute

Hardware-agnostic compute fabric discovery for Koan. Detects GPUs, enumerates available runtimes (Ollama, ONNX, Python), and resolves the best placement for AI workloads across local and network resources.

- Target framework: net10.0
- License: Apache-2.0
- Version: 0.6.3

## Install

```powershell
dotnet add package Sylin.Koan.AI.Compute
```

## Quick Start

```csharp
// Check what's available on this machine
var resources = await Compute.Available();
foreach (var r in resources)
    Console.WriteLine($"{r.DeviceName} [{r.Accelerator}] {r.VramBytes / 1_073_741_824}GB VRAM");

// Resolve best compute for a workload
var resolution = await Compute.Resolve(new ComputeRequirement
{
    Accelerator = Accelerator.CUDA,
    MinVramBytes = 8L * 1024 * 1024 * 1024,  // 8 GB
    Capabilities = [ComputeCapability.Inference]
});

if (resolution.Target is not null)
    Console.WriteLine($"Using: {resolution.Target.DeviceName}");
```

## Core API

```csharp
Compute.Available()                          // All detected compute resources
Compute.Fleet()                              // Full fleet including network nodes
Compute.Resolve(ComputeRequirement req)      // Best placement → ComputeResolution
Compute.Check(ComputeRequirement req)        // bool — requirement satisfiable?
Compute.Require(ComputeRequirement req)      // Throws if requirement not met
Compute.Prefer(ComputeRequirement req)       // Returns best or null (no throw)
```

## `ComputeResource`

```csharp
public sealed class ComputeResource
{
    string Id              { get; }
    Accelerator Accelerator { get; }  // None | CUDA | ROCm | Metal | DirectML | OneAPI
    long VramBytes         { get; }
    string DeviceName      { get; }
    ComputeLocation Location { get; } // Local | Network
    IReadOnlyList<string> Runtimes { get; }  // "ollama", "onnx", "python", etc.
    string Status          { get; }
}
```

## Accelerators

| Value | Hardware |
|-------|---------|
| `None` | CPU-only |
| `CUDA` | NVIDIA GPU |
| `ROCm` | AMD GPU |
| `Metal` | Apple Silicon |
| `DirectML` | Windows ML (any GPU) |
| `OneAPI` | Intel GPU |

## Detection Sources

- **NVIDIA**: `nvidia-smi`, CUDA environment variables
- **AMD**: ROCm sysfs (`/sys/class/drm/`)
- **Apple**: Metal framework availability
- **Runtimes**: Ollama process detection, Python environment, ONNX Runtime presence

## Reference

- **Related**: `Koan.AI.Models` (model deployment routing), `Koan.AI` (pipeline facade)
