use anyhow::{Context, Result};
use sysinfo::System;
#[cfg(not(target_os = "windows"))]
use std::fs;
use std::process::Command;
use garden_common::{format_bytes, format_uptime, AiRuntime, CpuMetrics, DiskMetrics, GpuInfo, MemoryMetrics, StoneResources};

/// Collect CPU model and features from /proc/cpuinfo (Linux) or WMI (Windows)
pub fn get_cpu_info() -> Result<(String, Vec<String>, String)> {
    #[cfg(target_os = "windows")]
    {
        get_cpu_info_windows()
    }
    
    #[cfg(not(target_os = "windows"))]
    {
        get_cpu_info_linux()
    }
}

#[cfg(not(target_os = "windows"))]
fn get_cpu_info_linux() -> Result<(String, Vec<String>, String)> {
    let cpuinfo = fs::read_to_string("/proc/cpuinfo")
        .context("Failed to read /proc/cpuinfo")?;

    let mut model_name = String::from("Unknown");
    let mut features = Vec::new();
    let mut architecture = std::env::consts::ARCH.to_string();

    for line in cpuinfo.lines() {
        if line.starts_with("model name") {
            if let Some(value) = line.split(':').nth(1) {
                model_name = value.trim().to_string();
            }
        } else if line.starts_with("flags") || line.starts_with("Features") {
            if let Some(flags_str) = line.split(':').nth(1) {
                features = flags_str
                    .split_whitespace()
                    .map(|s| s.to_lowercase())
                    .collect();
            }
        }
    }

    // For ARM, try to get more specific arch info
    if architecture.starts_with("arm") || architecture.starts_with("aarch") {
        if let Ok(arch_info) = fs::read_to_string("/proc/device-tree/model") {
            architecture = arch_info.trim().to_string();
        }
    }

    Ok((model_name, features, architecture))
}

#[cfg(target_os = "windows")]
fn get_cpu_info_windows() -> Result<(String, Vec<String>, String)> {
    let mut model_name = String::from("Unknown");
    let architecture = std::env::consts::ARCH.to_string();
    let mut features = Vec::new();
    
    // Get CPU model name from WMI
    let output = Command::new("powershell")
        .args(&["-Command", "Get-WmiObject -Class Win32_Processor | Select-Object -ExpandProperty Name"])
        .output();
    
    if let Ok(output) = output {
        if let Ok(name) = String::from_utf8(output.stdout) {
            model_name = name.trim().to_string();
        }
    }
    
    // Detect CPU features using CPUID - is_x86_feature_detected! is safe
    {
        // Check basic features
        if is_x86_feature_detected!("sse") { features.push("sse".to_string()); }
        if is_x86_feature_detected!("sse2") { features.push("sse2".to_string()); }
        if is_x86_feature_detected!("sse3") { features.push("sse3".to_string()); }
        if is_x86_feature_detected!("ssse3") { features.push("ssse3".to_string()); }
        if is_x86_feature_detected!("sse4.1") { features.push("sse4_1".to_string()); }
        if is_x86_feature_detected!("sse4.2") { features.push("sse4_2".to_string()); }
        if is_x86_feature_detected!("avx") { features.push("avx".to_string()); }
        if is_x86_feature_detected!("avx2") { features.push("avx2".to_string()); }
        if is_x86_feature_detected!("fma") { features.push("fma".to_string()); }
        if is_x86_feature_detected!("bmi1") { features.push("bmi1".to_string()); }
        if is_x86_feature_detected!("bmi2") { features.push("bmi2".to_string()); }
        if is_x86_feature_detected!("aes") { features.push("aes".to_string()); }
        if is_x86_feature_detected!("avx512f") { features.push("avx512f".to_string()); }
        if is_x86_feature_detected!("avx512bw") { features.push("avx512bw".to_string()); }
        if is_x86_feature_detected!("avx512cd") { features.push("avx512cd".to_string()); }
        if is_x86_feature_detected!("avx512dq") { features.push("avx512dq".to_string()); }
        if is_x86_feature_detected!("avx512vl") { features.push("avx512vl".to_string()); }
    }
    
    Ok((model_name, features, architecture))
}

/// Collect host-level resource metrics
pub fn collect_stone_resources() -> Result<StoneResources> {
    let mut system = System::new_all();
    system.refresh_all();

    // CPU metrics
    let usage_percent = system.global_cpu_info().cpu_usage();
    let cpu = CpuMetrics {
        cores: system.cpus().len(),
        usage_percent,
        usage_friendly: format!("{:.1}%", usage_percent),
    };

    // Memory metrics
    let total_bytes = system.total_memory();
    let used_bytes = system.used_memory();
    let available_bytes = system.available_memory();
    let used_percent = if total_bytes > 0 {
        (used_bytes as f64 / total_bytes as f64 * 100.0) as f32
    } else {
        0.0
    };
    let memory = MemoryMetrics {
        total_bytes,
        used_bytes,
        available_bytes,
        used_percent,
        total_friendly: format_bytes(total_bytes),
        used_friendly: format_bytes(used_bytes),
        available_friendly: format_bytes(available_bytes),
    };

    // Disk metrics - focus on root filesystem or /var/lib/zen-garden if available
    let disks = sysinfo::Disks::new_with_refreshed_list();
    let disk = disks
        .iter()
        .find(|d| {
            let mount_point = d.mount_point().to_string_lossy();
            mount_point == "/var/lib/zen-garden" || mount_point == "/"
        })
        .or_else(|| disks.iter().next())
        .map(|d| {
            let total = d.total_space();
            let available = d.available_space();
            let used = total - available;
            let used_percent = if total > 0 {
                (used as f64 / total as f64 * 100.0) as f32
            } else {
                0.0
            };
            DiskMetrics {
                total_bytes: total,
                used_bytes: used,
                available_bytes: available,
                used_percent,
                path: d.mount_point().to_string_lossy().to_string(),
                total_friendly: format_bytes(total),
                used_friendly: format_bytes(used),
                available_friendly: format_bytes(available),
            }
        })
        .context("No disk information available")?;

    // System uptime
    let uptime_seconds = sysinfo::System::uptime();
    let uptime_friendly = format_uptime(uptime_seconds);

    Ok(StoneResources {
        cpu,
        memory,
        disk,
        uptime_seconds,
        uptime_friendly,
    })
}

/// Best-effort disk type detection for a mount point.
///
/// Returns one of: "NVMe", "SSD", "HDD" (or None if unknown).
pub fn detect_disk_type_for_mount(mount_point: &str) -> Option<String> {
    #[cfg(target_os = "windows")]
    {
        let _ = mount_point;
        None
    }

    #[cfg(not(target_os = "windows"))]
    {
        // findmnt is widely available on Debian/Ubuntu.
        let output = Command::new("findmnt")
            .args(["-no", "SOURCE", "--target", mount_point])
            .output()
            .ok()?;

        if !output.status.success() {
            return None;
        }

        let source = String::from_utf8_lossy(&output.stdout).trim().to_string();
        if source.is_empty() {
            return None;
        }

        // Normalize /dev/<device><partition> to base block device name.
        // Examples:
        //   /dev/sda1 -> sda
        //   /dev/mmcblk0p2 -> mmcblk0
        //   /dev/nvme0n1p2 -> nvme0n1
        let dev = source.rsplit('/').next().unwrap_or("");
        if dev.is_empty() {
            return None;
        }

        let base = if dev.starts_with("nvme") {
            dev.split('p').next().unwrap_or(dev)
        } else if dev.starts_with("mmcblk") {
            dev.split('p').next().unwrap_or(dev)
        } else {
            dev.trim_end_matches(|c: char| c.is_ascii_digit())
        };

        if base.is_empty() {
            return None;
        }

        if base.starts_with("nvme") {
            return Some("NVMe".to_string());
        }

        let rotational_path = format!("/sys/block/{}/queue/rotational", base);
        let rotational = fs::read_to_string(rotational_path).ok()?;
        match rotational.trim() {
            "0" => Some("SSD".to_string()),
            "1" => Some("HDD".to_string()),
            _ => None,
        }
    }
}

/// Detect GPU hardware
pub fn detect_gpus() -> Vec<GpuInfo> {
    let mut gpus = Vec::new();
    
    // Try NVIDIA detection first (most common for AI workloads)
    if let Ok(nvidia_gpus) = detect_nvidia_gpus() {
        gpus.extend(nvidia_gpus);
    }
    
    // Try AMD detection
    if let Ok(amd_gpus) = detect_amd_gpus() {
        gpus.extend(amd_gpus);
    }
    
    // Try Intel detection
    if let Ok(intel_gpus) = detect_intel_gpus() {
        gpus.extend(intel_gpus);
    }
    
    // If nothing detected but we're on Windows, try DirectX/DXGI detection
    #[cfg(target_os = "windows")]
    {
        if gpus.is_empty() {
            if let Ok(dxgi_gpus) = detect_windows_gpus() {
                gpus.extend(dxgi_gpus);
            }
        }
    }
    
    // Detect actual AI runtime installations for each GPU
    for gpu in &mut gpus {
        // Detect runtime and convert directly to dual-format strings
        if let Some(runtime) = detect_ai_runtime(&gpu.vendor, &gpu.capabilities) {
            gpu.ai_runtimes = ai_runtime_to_strings(&runtime);
        }
    }

    // Scan for containerized AI runtimes (Docker/Podman images)
    // This validates against hardware: ROCm requires AMD, CUDA requires NVIDIA
    let container_runtimes = scan_ai_runtime_containers(&gpus);

    // Merge containerized runtimes into each GPU's runtime list
    // Container runtimes are system-wide but hardware-validated
    if !container_runtimes.is_empty() {
        for gpu in &mut gpus {
            // Only add containerized runtimes that match this GPU's vendor
            for runtime in &container_runtimes {
                let runtime_lower = runtime.to_lowercase();

                // Vendor-specific runtime matching
                let matches_vendor = if runtime_lower.starts_with("rocm") {
                    gpu.vendor.to_lowercase() == "amd"
                } else if runtime_lower.starts_with("cuda") {
                    gpu.vendor.to_lowercase() == "nvidia"
                } else {
                    // Generic runtimes (tensorflow, pytorch) available to all GPUs
                    true
                };

                if matches_vendor && !gpu.ai_runtimes.contains(runtime) {
                    gpu.ai_runtimes.push(runtime.clone());
                }
            }
        }
    }

    gpus
}

fn detect_nvidia_gpus() -> Result<Vec<GpuInfo>> {
    let output = Command::new("nvidia-smi")
        .args([
            "--query-gpu=name,memory.total",
            "--format=csv,noheader,nounits"
        ])
        .output()?;
    
    if !output.status.success() {
        anyhow::bail!("nvidia-smi failed");
    }
    
    let stdout = String::from_utf8_lossy(&output.stdout);
    let gpus = stdout
        .lines()
        .filter_map(|line| {
            let parts: Vec<&str> = line.split(',').map(|s| s.trim()).collect();
            if parts.len() >= 2 {
                let model = parts[0].to_string();
                let vram_mb = parts[1].parse::<u64>().ok();
                
                let mut capabilities = vec!["cuda".to_string(), "vulkan".to_string()];
                if cfg!(target_os = "windows") {
                    capabilities.push("directml".to_string());
                }
                
                Some(GpuInfo {
                    vendor: "NVIDIA".to_string(),
                    model,
                    vram_mb,
                    capabilities,
                    ai_runtimes: Vec::new(),
                })
            } else {
                None
            }
        })
        .collect();
    
    Ok(gpus)
}

fn detect_amd_gpus() -> Result<Vec<GpuInfo>> {
    // Try rocm-smi first
    if let Ok(output) = Command::new("rocm-smi")
        .arg("--showproductname")
        .output() {
        if output.status.success() {
            let stdout = String::from_utf8_lossy(&output.stdout);
            let gpus: Vec<GpuInfo> = stdout
                .lines()
                .filter(|line| line.contains("Card series"))
                .filter_map(|line| {
                    let model = line.split(':').nth(1)?.trim().to_string();
                    
                    let mut capabilities = vec!["rocm".to_string(), "vulkan".to_string()];
                    if cfg!(target_os = "windows") {
                        capabilities.push("directml".to_string());
                    }
                    
                    Some(GpuInfo {
                        vendor: "AMD".to_string(),
                        model,
                        vram_mb: None, // Would need additional query
                        capabilities,
                        ai_runtimes: Vec::new(),
                    })
                })
                .collect();
            
            if !gpus.is_empty() {
                return Ok(gpus);
            }
        }
    }
    
    // Fallback: check lspci on Linux
    #[cfg(not(target_os = "windows"))]
    {
        if let Ok(output) = Command::new("lspci").output() {
            let stdout = String::from_utf8_lossy(&output.stdout);
            let gpus: Vec<GpuInfo> = stdout
                .lines()
                .filter(|line| line.contains("VGA") || line.contains("3D"))
                .filter(|line| line.to_lowercase().contains("amd") || line.to_lowercase().contains("radeon"))
                .map(|line| {
                    let model = line.split(':').last()
                        .unwrap_or("AMD GPU")
                        .trim()
                        .to_string();
                    
                    GpuInfo {
                        vendor: "AMD".to_string(),
                        model,
                        vram_mb: None,
                        capabilities: vec!["vulkan".to_string()], // Unknown without rocm-smi
                        ai_runtimes: Vec::new(),
                    }
                })
                .collect();
            
            if !gpus.is_empty() {
                return Ok(gpus);
            }
        }
    }
    
    anyhow::bail!("No AMD GPUs detected")
}

fn detect_intel_gpus() -> Result<Vec<GpuInfo>> {
    #[cfg(not(target_os = "windows"))]
    {
        if let Ok(output) = Command::new("lspci").output() {
            let stdout = String::from_utf8_lossy(&output.stdout);
            let gpus: Vec<GpuInfo> = stdout
                .lines()
                .filter(|line| line.contains("VGA") || line.contains("3D"))
                .filter(|line| line.to_lowercase().contains("intel"))
                .map(|line| {
                    let model = line.split(':').last()
                        .unwrap_or("Intel GPU")
                        .trim()
                        .to_string();
                    
                    GpuInfo {
                        vendor: "Intel".to_string(),
                        model,
                        vram_mb: None,
                        capabilities: vec!["vulkan".to_string()],
                        ai_runtimes: Vec::new(),
                    }
                })
                .collect();
            
            if !gpus.is_empty() {
                return Ok(gpus);
            }
        }
    }
    
    anyhow::bail!("No Intel GPUs detected")
}

#[cfg(target_os = "windows")]
fn detect_windows_gpus() -> Result<Vec<GpuInfo>> {
    // Query WMI for GPU info including PNPDeviceID to filter out non-PCIe devices
    let output = Command::new("powershell")
        .args([
            "-NoProfile",
            "-Command",
            "Get-WmiObject Win32_VideoController | Select-Object Name, AdapterRAM, PNPDeviceID, AdapterCompatibility | ConvertTo-Json"
        ])
        .output()?;
    
    if !output.status.success() {
        anyhow::bail!("PowerShell GPU query failed");
    }
    
    let stdout = String::from_utf8_lossy(&output.stdout);

    // Get accurate VRAM using DXGI (native DirectX API - most reliable)
    // Falls back to WMI+Registry if DXGI fails
    let dxgi_vram = get_vram_from_dxgi();
    let wmi_vram = if dxgi_vram.is_empty() {
        tracing::info!("DXGI VRAM detection failed, falling back to WMI");
        get_vram_from_wmi()
    } else {
        tracing::info!("Using DXGI for accurate VRAM detection");
        dxgi_vram
    };

    // Parse JSON output
    if let Ok(json) = serde_json::from_str::<serde_json::Value>(&stdout) {
        let gpu_array: Vec<&serde_json::Value> = if json.is_array() {
            json.as_array().unwrap().iter().collect()
        } else {
            vec![&json]
        };

        let gpus: Vec<GpuInfo> = gpu_array.iter()
        .filter_map(|gpu| {
            let name = gpu["Name"].as_str()?.to_string();
            let pnp_id = gpu["PNPDeviceID"].as_str().unwrap_or("");
            let adapter_compat = gpu["AdapterCompatibility"].as_str().unwrap_or("");

            // Filter out non-compute GPUs:
            // - USB devices (DisplayLink, etc.)
            // - Microsoft Basic Display Adapter
            // - Virtual/software renderers
            if pnp_id.starts_with("USB\\") {
                tracing::debug!("Skipping USB display adapter: {}", name);
                return None;
            }
            if name.contains("DisplayLink") || name.contains("Basic Display") || name.contains("Microsoft Basic") {
                tracing::debug!("Skipping virtual/display-only adapter: {}", name);
                return None;
            }

            let vram_bytes = gpu["AdapterRAM"].as_u64();
            let mut vram_mb = vram_bytes.map(|b| b / 1024 / 1024);

            // If WMI AdapterRAM is unreliable, try to get from enhanced WMI detection
            // Trigger fallback if: None, < 1GB, or 4000-4096 MB (indicates 32-bit truncation at 4GB)
            // Try both exact and normalized name matching
            let needs_fallback = vram_mb.is_none()
                || vram_mb.unwrap() < 1024
                || (vram_mb.unwrap() >= 4000 && vram_mb.unwrap() <= 4096);

            if needs_fallback {
                let normalized_name = normalize_gpu_name(&name);
                if let Some(enhanced_vram) = wmi_vram.get(&name)
                    .or_else(|| wmi_vram.get(&normalized_name)) {
                    vram_mb = Some(*enhanced_vram);
                }
            }
            
            // Detect vendor and capabilities from name and compatibility
            let name_lower = name.to_lowercase();
            let compat_lower = adapter_compat.to_lowercase();
            
            let (vendor, capabilities): (&str, Vec<String>) = 
                if name_lower.contains("nvidia") || name_lower.contains("geforce") || name_lower.contains("quadro") || name_lower.contains("rtx") {
                    ("NVIDIA", vec!["cuda".to_string(), "vulkan".to_string(), "directml".to_string()])
                } else if name_lower.contains("amd") || name_lower.contains("radeon") || compat_lower.contains("advanced micro devices") {
                    ("AMD", vec!["vulkan".to_string(), "directml".to_string()])
                } else if name_lower.contains("intel") && pnp_id.starts_with("PCI\\") {
                    ("Intel", vec!["vulkan".to_string(), "directml".to_string()])
                } else {
                    // Unknown but on PCIe bus - might still be usable
                    if pnp_id.starts_with("PCI\\") {
                        ("Unknown", vec!["vulkan".to_string()])
                    } else {
                        return None; // Not a real GPU
                    }
                };
            
            Some(GpuInfo {
                vendor: vendor.to_string(),
                model: name,
                vram_mb,
                capabilities,
                ai_runtimes: Vec::new(),
            })
        })
        .collect();
        
        if !gpus.is_empty() {
            return Ok(gpus);
        }
    }
    
    anyhow::bail!("No compute-capable GPUs detected")
}

/// Normalize GPU name for consistent matching between detection methods
#[cfg(target_os = "windows")]
fn normalize_gpu_name(name: &str) -> String {
    // Remove extra whitespace, lowercase, and normalize vendor prefixes
    name.to_lowercase()
        .split_whitespace()
        .filter(|s| !s.is_empty())
        .collect::<Vec<_>>()
        .join(" ")
}

/// Get accurate VRAM sizes using WMI + PowerShell (more reliable than DXDiag)
#[cfg(target_os = "windows")]
fn get_vram_from_wmi() -> std::collections::HashMap<String, u64> {
    use std::collections::HashMap;

    let mut vram_map = HashMap::new();

    // Use WMI to get video controller information with dedicated memory
    // This approach is faster and more reliable than DXDiag text parsing
    let output = Command::new("powershell")
        .args([
            "-NoProfile",
            "-Command",
            r#"
            Get-CimInstance -ClassName Win32_VideoController | Where-Object {
                $_.PNPDeviceID -like "PCI\VEN*" -and
                $_.Name -notlike "*Basic*" -and
                $_.Name -notlike "*DisplayLink*"
            } | ForEach-Object {
                $vramBytes = $_.AdapterRAM

                # WMI AdapterRAM can be unreliable (32-bit field capped at ~4GB), try to get from device properties
                # Trigger fallback if: null, zero, < 1GB, or suspiciously close to 4GB (4000-4096 MB indicates truncation)
                $vramMB = [Math]::Round($vramBytes / 1MB)
                if ($vramBytes -eq $null -or $vramBytes -eq 0 -or $vramBytes -lt 1GB -or ($vramMB -ge 4000 -and $vramMB -le 4096)) {
                    # Try to read from registry for more accurate VRAM info
                    $pnpId = $_.PNPDeviceID
                    $regPath = "HKLM:\SYSTEM\CurrentControlSet\Enum\$pnpId"
                    if (Test-Path $regPath) {
                        try {
                            $hwInfo = Get-ItemProperty -Path $regPath -ErrorAction SilentlyContinue
                            if ($hwInfo.'HardwareInformation.qwMemorySize') {
                                $vramBytes = $hwInfo.'HardwareInformation.qwMemorySize'
                            }
                        } catch {}
                    }
                }

                # If still no valid VRAM and AdapterRAM is capped at 4GB, it's likely truncated
                # Use a heuristic: if it's exactly 4095 MB, the actual value is likely higher
                if ($vramBytes -gt 0) {
                    [PSCustomObject]@{
                        Name = $_.Name
                        VramMB = [Math]::Round($vramBytes / 1MB)
                    }
                }
            } | ConvertTo-Json
            "#
        ])
        .output();

    if let Ok(output) = output {
        if let Ok(stdout) = String::from_utf8(output.stdout) {
            // Parse JSON output
            if let Ok(json) = serde_json::from_str::<serde_json::Value>(&stdout) {
                let gpu_array: Vec<&serde_json::Value> = if json.is_array() {
                    json.as_array().unwrap().iter().collect()
                } else {
                    vec![&json]
                };

                for gpu in gpu_array {
                    if let (Some(name), Some(vram)) = (
                        gpu["Name"].as_str(),
                        gpu["VramMB"].as_u64()
                    ) {
                        // Only add if VRAM is reasonable (> 0)
                        if vram > 0 {
                            let normalized = normalize_gpu_name(name);
                            vram_map.insert(normalized, vram);

                            // Also store original name for fallback matching
                            vram_map.insert(name.to_string(), vram);
                        }
                    }
                }
            }
        }
    }

    vram_map
}

/// Get accurate VRAM using native DXGI API (most reliable on Windows)
///
/// Returns HashMap of GPU description -> VRAM in MB
/// Uses DirectX Graphics Infrastructure to query dedicated video memory directly
#[cfg(target_os = "windows")]
fn get_vram_from_dxgi() -> std::collections::HashMap<String, u64> {
    use std::collections::HashMap;
    use std::mem::MaybeUninit;
    use windows::Win32::Graphics::Dxgi::{
        CreateDXGIFactory, IDXGIFactory, DXGI_ADAPTER_DESC, DXGI_ERROR_NOT_FOUND,
    };

    let mut vram_map = HashMap::new();

    // Create DXGI Factory
    let factory: Result<IDXGIFactory, _> = unsafe { CreateDXGIFactory() };

    let Ok(factory) = factory else {
        tracing::debug!("Failed to create DXGI factory");
        return vram_map;
    };

    // Enumerate adapters
    let mut adapter_index = 0;
    loop {
        let adapter = unsafe { factory.EnumAdapters(adapter_index) };

        match adapter {
            Ok(adapter) => {
                // Get adapter description using mutable pointer
                let mut desc = MaybeUninit::<DXGI_ADAPTER_DESC>::uninit();
                let result = unsafe { adapter.GetDesc(desc.as_mut_ptr()) };

                if result.is_ok() {
                    let desc = unsafe { desc.assume_init() };

                    // Convert wide string to Rust String
                    let description = String::from_utf16_lossy(&desc.Description)
                        .trim_end_matches('\0')
                        .to_string();

                    // Get dedicated video memory (in bytes, 64-bit value - no truncation!)
                    let vram_bytes = desc.DedicatedVideoMemory as u64;
                    let vram_mb: u64 = vram_bytes / (1024 * 1024);

                    if vram_mb > 0 {
                        tracing::debug!(
                            "DXGI Adapter {}: {} - {} MB VRAM",
                            adapter_index,
                            description,
                            vram_mb
                        );

                        // Store both original and normalized names
                        vram_map.insert(description.clone(), vram_mb);
                        vram_map.insert(normalize_gpu_name(&description), vram_mb);
                    }
                }

                adapter_index += 1;
            }
            Err(e) => {
                // DXGI_ERROR_NOT_FOUND means we've enumerated all adapters
                if e.code() == DXGI_ERROR_NOT_FOUND {
                    break;
                }
                tracing::debug!("Error enumerating DXGI adapter {}: {:?}", adapter_index, e);
                break;
            }
        }
    }

    if !vram_map.is_empty() {
        tracing::info!("DXGI detected {} GPU(s) with accurate VRAM data", vram_map.len() / 2);
    }

    vram_map
}

/// Detect actual AI runtime installations for a GPU
/// This checks for real toolkit/driver installations, not just hardware capabilities
fn detect_ai_runtime(vendor: &str, capabilities: &[String]) -> Option<AiRuntime> {
    let mut runtime = AiRuntime::default();
    let mut has_any = false;
    
    // NVIDIA: Check for CUDA toolkit installation
    if vendor == "NVIDIA" || capabilities.iter().any(|c| c == "cuda") {
        if let Some(cuda_version) = detect_cuda_toolkit() {
            runtime.cuda_version = Some(cuda_version);
            has_any = true;
        }
    }
    
    // AMD: Check for ROCm installation
    if vendor == "AMD" || capabilities.iter().any(|c| c == "rocm") {
        if let Some(rocm_version) = detect_rocm_installation() {
            runtime.rocm_version = Some(rocm_version);
            has_any = true;
        }
    }
    
    // Windows: Check for DirectML (part of Windows ML)
    #[cfg(target_os = "windows")]
    {
        if detect_directml() {
            runtime.has_directml = true;
            has_any = true;
        }
    }
    
    // Intel: Check for OpenVINO
    if vendor == "Intel" {
        if detect_openvino() {
            runtime.has_openvino = true;
            has_any = true;
        }
    }
    
    if has_any {
        Some(runtime)
    } else {
        None
    }
}

/// Convert AiRuntime struct to dual-format runtime strings
/// Returns both simple ("cuda") and versioned ("cuda:12.2") formats
fn ai_runtime_to_strings(runtime: &AiRuntime) -> Vec<String> {
    let mut runtimes = Vec::new();

    // CUDA
    if let Some(cuda_version) = &runtime.cuda_version {
        runtimes.push("cuda".to_string());
        runtimes.push(format!("cuda:{}", cuda_version));
    }

    // ROCm
    if let Some(rocm_version) = &runtime.rocm_version {
        runtimes.push("rocm".to_string());
        runtimes.push(format!("rocm:{}", rocm_version));
    }

    // DirectML (no version tracking)
    if runtime.has_directml {
        runtimes.push("directml".to_string());
    }

    // OpenVINO (no version tracking)
    if runtime.has_openvino {
        runtimes.push("openvino".to_string());
    }

    runtimes
}

/// Detect CUDA toolkit installation (not just driver)
fn detect_cuda_toolkit() -> Option<String> {
    // Try nvcc --version (compiler presence indicates toolkit)
    if let Ok(output) = Command::new("nvcc")
        .arg("--version")
        .output() {
        if output.status.success() {
            let stdout = String::from_utf8_lossy(&output.stdout);
            // Parse version from: "Cuda compilation tools, release 12.2, V12.2.140"
            if let Some(line) = stdout.lines().find(|l| l.contains("release")) {
                if let Some(version_part) = line.split("release").nth(1) {
                    let version = version_part.trim().split(',').next()?
                        .trim()
                        .to_string();
                    return Some(version);
                }
            }
        }
    }
    
    // Check environment variable (toolkit sets this)
    if let Ok(cuda_path) = std::env::var("CUDA_PATH") {
        if !cuda_path.is_empty() {
            // Try to extract version from path like "C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.2"
            if let Some(version) = cuda_path.split("v").last() {
                if version.chars().next().map_or(false, |c| c.is_numeric()) {
                    return Some(version.to_string());
                }
            }
            // Fallback: just indicate toolkit is present
            return Some("installed".to_string());
        }
    }
    
    None
}

/// Detect ROCm installation
fn detect_rocm_installation() -> Option<String> {
    // Try rocm-smi --version
    if let Ok(output) = Command::new("rocm-smi")
        .arg("--version")
        .output() {
        if output.status.success() {
            let stdout = String::from_utf8_lossy(&output.stdout);
            if let Some(line) = stdout.lines().find(|l| l.contains("version")) {
                if let Some(version) = line.split_whitespace().last() {
                    return Some(version.trim().to_string());
                }
            }
            return Some("installed".to_string());
        }
    }
    
    // Check for ROCm paths
    #[cfg(target_os = "linux")]
    {
        if std::path::Path::new("/opt/rocm").exists() {
            // Try to read version file
            if let Ok(version_content) = std::fs::read_to_string("/opt/rocm/.info/version") {
                return Some(version_content.trim().to_string());
            }
            return Some("installed".to_string());
        }
    }
    
    None
}

/// Detect DirectML availability on Windows
#[cfg(target_os = "windows")]
fn detect_directml() -> bool {
    // DirectML is part of Windows 10 1903+ and comes with DirectX 12
    // Check if DirectML.dll is available (shipped with Windows ML)
    let system_dir = std::env::var("SystemRoot")
        .unwrap_or_else(|_| "C:\\Windows".to_string());
    
    let directml_paths = [
        format!("{}\\System32\\DirectML.dll", system_dir),
        format!("{}\\SysWOW64\\DirectML.dll", system_dir),
    ];
    
    directml_paths.iter().any(|path| std::path::Path::new(path).exists())
}

#[cfg(not(target_os = "windows"))]
#[allow(dead_code)]
fn detect_directml() -> bool {
    false
}

/// Detect OpenVINO toolkit installation
fn detect_openvino() -> bool {
    // Check for OpenVINO environment variable
    if std::env::var("INTEL_OPENVINO_DIR").is_ok() {
        return true;
    }
    
    // Check common installation paths
    #[cfg(target_os = "windows")]
    {
        let common_paths = [
            "C:\\Program Files (x86)\\Intel\\openvino",
            "C:\\Program Files\\Intel\\openvino",
        ];
        if common_paths.iter().any(|p| std::path::Path::new(p).exists()) {
            return true;
        }
    }
    
    #[cfg(target_os = "linux")]
    {
        let home_openvino = std::env::var("HOME").ok()
            .map(|h| format!("{}/intel/openvino", h))
            .unwrap_or_default();
        let common_paths = [
            "/opt/intel/openvino",
            home_openvino.as_str(),
        ];
        if common_paths.iter().any(|p| !p.is_empty() && std::path::Path::new(p).exists()) {
            return true;
        }
    }
    
    false
}

/// Detect storage devices with partition count and usage
pub fn detect_storage() -> Vec<garden_common::StorageDevice> {
    #[cfg(target_os = "windows")]
    {
        detect_storage_windows()
    }
    
    #[cfg(target_os = "linux")]
    {
        detect_storage_linux()
    }
    
    #[cfg(not(any(target_os = "windows", target_os = "linux")))]
    {
        vec![]
    }
}

#[cfg(target_os = "windows")]
fn detect_storage_windows() -> Vec<garden_common::StorageDevice> {
    use garden_common::{StorageDevice, DiskType};
    let mut devices = Vec::new();
    
    // PowerShell command to get disk info
    let output = Command::new("powershell")
        .args(&["-Command", 
            "Get-PhysicalDisk | Select-Object DeviceId,MediaType,Size | ConvertTo-Json"])
        .output();
    
    let output = match output {
        Ok(o) => o,
        Err(e) => {
            tracing::warn!("Failed to execute PowerShell command for storage detection: {}", e);
            return devices;
        }
    };
    
    let json_str = match String::from_utf8(output.stdout) {
        Ok(s) => s,
        Err(e) => {
            tracing::warn!("Failed to parse PowerShell output as UTF-8: {}", e);
            return devices;
        }
    };
    
    let json: serde_json::Value = match serde_json::from_str(&json_str) {
        Ok(j) => j,
        Err(e) => {
            tracing::warn!("Failed to parse JSON from PowerShell: {}", e);
            return devices;
        }
    };
    
    let disks = if json.is_array() {
        json.as_array().unwrap().clone()
    } else {
        vec![json]
    };
    
    for disk in disks {
        let id = match disk.get("DeviceId").and_then(|v| v.as_str()) {
            Some(s) => s.to_string(),
            None => {
                if let Some(id_num) = disk.get("DeviceId").and_then(|v| v.as_u64()) {
                    id_num.to_string()
                } else {
                    continue;
                }
            }
        };
        
        let size = match disk.get("Size").and_then(|v| v.as_u64()) {
            Some(s) => s,
            None => continue,
        };
        
        let media_type = disk.get("MediaType")
            .and_then(|v| v.as_str())
            .unwrap_or("Unknown");
        
        let disk_type = match media_type {
            "SSD" => DiskType::SSD,
            "HDD" => DiskType::HDD,
            s if s.contains("NVMe") => DiskType::NVMe,
            _ => DiskType::Unknown,
        };
        
        // Get partition count for this disk
        let partition_cmd = format!(
            "(Get-Partition -DiskNumber {} -ErrorAction SilentlyContinue | Measure-Object).Count",
            id
        );
        let partition_count = Command::new("powershell")
            .args(&["-Command", &partition_cmd])
            .output()
            .ok()
            .and_then(|o| String::from_utf8(o.stdout).ok())
            .and_then(|s| s.trim().parse::<usize>().ok())
            .unwrap_or(0);
        
        // Get used space percentage (average across all volumes on this disk)
        let usage_cmd = format!(
            "$vols = Get-Partition -DiskNumber {} -ErrorAction SilentlyContinue | Get-Volume -ErrorAction SilentlyContinue | Where-Object {{ $_.Size -gt 0 }}; if ($vols) {{ ($vols | ForEach-Object {{ [math]::Round(($_.Size - $_.SizeRemaining) / $_.Size * 100, 1) }} | Measure-Object -Average).Average }} else {{ 0 }}",
            id
        );
        let used_percent = Command::new("powershell")
            .args(&["-Command", &usage_cmd])
            .output()
            .ok()
            .and_then(|o| String::from_utf8(o.stdout).ok())
            .and_then(|s| s.trim().parse::<f32>().ok())
            .unwrap_or(0.0);
        
        devices.push(StorageDevice {
            identifier: format!("Disk {}", id),
            size_gb: size / 1_000_000_000,  // Use decimal GB (1000^3) not binary
            disk_type,
            partition_count,
            used_percent,
        });
    }
    
    devices
}

#[cfg(target_os = "linux")]
fn detect_storage_linux() -> Vec<garden_common::StorageDevice> {
    use garden_common::{StorageDevice, DiskType};
    let mut devices = Vec::new();
    
    // Read /proc/partitions to get disk list
    if let Ok(partitions) = fs::read_to_string("/proc/partitions") {
        for line in partitions.lines().skip(2) {  // Skip header
            let parts: Vec<&str> = line.split_whitespace().collect();
            if parts.len() >= 4 {
                let device_name = parts[3];
                // Only process whole disks (sda, nvme0n1, etc.), not partitions
                if device_name.chars().last().map(|c| c.is_ascii_digit()).unwrap_or(false) 
                    && !device_name.starts_with("nvme") {
                    continue;  // Skip partitions like sda1, sdb2
                }
                if device_name.starts_with("nvme") && device_name.contains('p') {
                    continue;  // Skip NVMe partitions like nvme0n1p1
                }
                
                // Get size from /sys/block
                let size_path = format!("/sys/block/{}/size", device_name);
                let size_sectors = fs::read_to_string(&size_path)
                    .ok()
                    .and_then(|s| s.trim().parse::<u64>().ok())
                    .unwrap_or(0);
                let size_gb = (size_sectors * 512) / 1024 / 1024 / 1024;
                
                // Detect disk type
                let rotational_path = format!("/sys/block/{}/queue/rotational", device_name);
                let is_rotational = fs::read_to_string(&rotational_path)
                    .ok()
                    .and_then(|s| s.trim().parse::<u8>().ok())
                    .unwrap_or(0) == 1;
                
                let disk_type = if device_name.starts_with("nvme") {
                    DiskType::NVMe
                } else if is_rotational {
                    DiskType::HDD
                } else {
                    DiskType::SSD
                };
                
                // Count partitions
                let partition_count = fs::read_dir(format!("/sys/block/{}", device_name))
                    .ok()
                    .map(|entries| {
                        entries.filter_map(Result::ok)
                            .filter(|e| e.file_name().to_string_lossy().starts_with(device_name))
                            .count()
                    })
                    .unwrap_or(0);
                
                // Calculate usage (approximate from df)
                let df_output = Command::new("df")
                    .args(&["-k", &format!("/dev/{}", device_name)])
                    .output()
                    .ok();
                let used_percent = if let Some(output) = df_output {
                    String::from_utf8(output.stdout)
                        .ok()
                        .and_then(|s| {
                            s.lines().nth(1).and_then(|line| {
                                line.split_whitespace().nth(4).and_then(|pct| {
                                    pct.trim_end_matches('%').parse::<f32>().ok()
                                })
                            })
                        })
                        .unwrap_or(0.0)
                } else {
                    0.0
                };
                
                devices.push(StorageDevice {
                    identifier: device_name.to_string(),
                    size_gb,
                    disk_type,
                    partition_count,
                    used_percent,
                });
            }
        }
    }
    
    devices
}

/// Detect OS version
pub fn detect_os_version() -> Option<String> {
    #[cfg(target_os = "windows")]
    {
        let output = Command::new("powershell")
            .args(&["-Command", 
                "(Get-WmiObject -Class Win32_OperatingSystem).Caption"])
            .output()
            .ok()?;
        String::from_utf8(output.stdout).ok()
            .map(|s| s.trim().to_string())
    }
    
    #[cfg(target_os = "linux")]
    {
        // Try /etc/os-release first
        if let Ok(content) = fs::read_to_string("/etc/os-release") {
            for line in content.lines() {
                if line.starts_with("PRETTY_NAME=") {
                    return Some(line.split('=').nth(1)?
                        .trim_matches('"')
                        .to_string());
                }
            }
        }
        None
    }
    
    #[cfg(not(any(target_os = "windows", target_os = "linux")))]
    {
        None
    }
}

/// Detect kernel version
pub fn detect_kernel_version() -> Option<String> {
    #[cfg(target_os = "windows")]
    {
        let output = Command::new("powershell")
            .args(&["-Command", 
                "(Get-WmiObject -Class Win32_OperatingSystem).Version"])
            .output()
            .ok()?;
        String::from_utf8(output.stdout).ok()
            .map(|s| s.trim().to_string())
    }
    
    #[cfg(target_os = "linux")]
    {
        let output = Command::new("uname")
            .arg("-r")
            .output()
            .ok()?;
        String::from_utf8(output.stdout).ok()
            .map(|s| s.trim().to_string())
    }
    
    #[cfg(not(any(target_os = "windows", target_os = "linux")))]
    {
        None
    }
}

/// Detect swap space in MB
pub fn detect_swap() -> Option<u64> {
    #[cfg(target_os = "linux")]
    {
        if let Ok(content) = fs::read_to_string("/proc/meminfo") {
            for line in content.lines() {
                if line.starts_with("SwapTotal:") {
                    return line.split_whitespace()
                        .nth(1)
                        .and_then(|s| s.parse::<u64>().ok())
                        .map(|kb| kb / 1024);  // Convert kB to MB
                }
            }
        }
        None
    }
    
    #[cfg(target_os = "windows")]
    {
        let output = Command::new("powershell")
            .args(&["-Command", 
                "(Get-WmiObject -Class Win32_PageFileUsage | Measure-Object -Property AllocatedBaseSize -Sum).Sum"])
            .output()
            .ok()?;
        String::from_utf8(output.stdout).ok()
            .and_then(|s| s.trim().parse::<u64>().ok())
    }
    
    #[cfg(not(any(target_os = "windows", target_os = "linux")))]
    {
        None
    }
}

#[cfg(not(target_os = "windows"))]
#[allow(dead_code)]
fn detect_windows_gpus() -> Result<Vec<GpuInfo>> {
    anyhow::bail!("Windows GPU detection not available on this platform")
}

/// Detect which container runtime is available (Docker or Podman)
pub fn detect_container_runtime() -> Option<String> {
    // Try Docker first (most common)
    if Command::new("docker")
        .arg("--version")
        .output()
        .map(|o| o.status.success())
        .unwrap_or(false)
    {
        return Some("docker".to_string());
    }

    // Try Podman
    if Command::new("podman")
        .arg("--version")
        .output()
        .map(|o| o.status.success())
        .unwrap_or(false)
    {
        return Some("podman".to_string());
    }

    None
}

/// Scan container images for AI runtimes
///
/// Returns detected runtimes in dual format (e.g., ["rocm", "rocm:5.7"])
///
/// # Hardware Validation
/// - ROCm runtimes are only included if AMD GPU is present
/// - CUDA runtimes are only included if NVIDIA GPU is present
/// - Generic AI frameworks (TensorFlow, PyTorch) are included if they have GPU tags
pub fn scan_ai_runtime_containers(gpus: &[GpuInfo]) -> Vec<String> {
    let mut container_runtimes = Vec::new();

    // Determine which GPUs are present
    let has_amd = gpus.iter().any(|g| g.vendor.to_lowercase() == "amd");
    let has_nvidia = gpus.iter().any(|g| g.vendor.to_lowercase() == "nvidia");

    // Detect container runtime
    let runtime_cmd = match detect_container_runtime() {
        Some(cmd) => cmd,
        None => {
            tracing::debug!("No container runtime detected (Docker/Podman)");
            return container_runtimes;
        }
    };

    tracing::info!("Scanning {} images for AI runtimes...", runtime_cmd);

    // Query images
    let output = Command::new(&runtime_cmd)
        .args(["images", "--format", "{{.Repository}}:{{.Tag}}"])
        .output();

    let Ok(output) = output else {
        tracing::warn!("Failed to query {} images", runtime_cmd);
        return container_runtimes;
    };

    if !output.status.success() {
        tracing::warn!("{} images command failed", runtime_cmd);
        return container_runtimes;
    }

    let stdout = String::from_utf8_lossy(&output.stdout);

    for line in stdout.lines() {
        let image = line.trim();
        if image.is_empty() || image == "<none>:<none>" {
            continue;
        }

        // Extract runtime information from image name
        if let Some(runtime_info) = extract_ai_runtime_from_image(image) {
            // Validate against hardware
            let (runtime_name, version) = runtime_info;

            match runtime_name.as_str() {
                "rocm" => {
                    if !has_amd {
                        tracing::debug!(
                            "Skipping ROCm container '{}' - no AMD GPU detected",
                            image
                        );
                        continue;
                    }
                }
                "cuda" => {
                    if !has_nvidia {
                        tracing::debug!(
                            "Skipping CUDA container '{}' - no NVIDIA GPU detected",
                            image
                        );
                        continue;
                    }
                }
                _ => {
                    // Other runtimes (TensorFlow, PyTorch) are vendor-agnostic
                }
            }

            // Add runtime in dual format
            if !container_runtimes.contains(&runtime_name) {
                container_runtimes.push(runtime_name.clone());
            }

            if let Some(ver) = version {
                let versioned = format!("{}:{}", runtime_name, ver);
                if !container_runtimes.contains(&versioned) {
                    container_runtimes.push(versioned);
                }
            }
        }
    }

    if !container_runtimes.is_empty() {
        tracing::info!(
            "Found {} containerized AI runtimes: {:?}",
            container_runtimes.len(),
            container_runtimes
        );
    }

    container_runtimes
}

/// Extract AI runtime information from container image name
///
/// Returns (runtime_name, optional_version)
///
/// # Examples
/// - `rocm/pytorch:rocm5.7_ubuntu22.04_py3.10` → Some(("rocm", Some("5.7")))
/// - `nvidia/cuda:12.2.0-base` → Some(("cuda", Some("12.2")))
/// - `tensorflow/tensorflow:latest-gpu` → Some(("tensorflow", None))
fn extract_ai_runtime_from_image(image: &str) -> Option<(String, Option<String>)> {
    let image_lower = image.to_lowercase();

    // ROCm images
    if image_lower.contains("rocm") {
        // Try to extract version from tag (e.g., rocm5.7, rocm/pytorch:rocm5.7_...)
        let version = if let Some(tag) = image.split(':').nth(1) {
            // Extract version like "rocm5.7" → "5.7"
            if let Some(rocm_part) = tag.split('_').find(|s| s.starts_with("rocm")) {
                rocm_part.trim_start_matches("rocm").split('_').next()
                    .filter(|v| !v.is_empty())
                    .map(|v| v.to_string())
            } else {
                None
            }
        } else {
            None
        };

        return Some(("rocm".to_string(), version));
    }

    // CUDA images
    if image_lower.contains("cuda") {
        let version = if let Some(tag) = image.split(':').nth(1) {
            // Extract version like "12.2.0-base" → "12.2"
            tag.split('-').next()
                .and_then(|v| {
                    // Take major.minor from semver (12.2.0 → 12.2)
                    let parts: Vec<&str> = v.split('.').collect();
                    if parts.len() >= 2 {
                        Some(format!("{}.{}", parts[0], parts[1]))
                    } else {
                        Some(v.to_string())
                    }
                })
        } else {
            None
        };

        return Some(("cuda".to_string(), version));
    }

    // TensorFlow GPU images
    if image_lower.contains("tensorflow") && (image_lower.contains("gpu") || image_lower.contains("cuda")) {
        return Some(("tensorflow".to_string(), None));
    }

    // PyTorch GPU images (if not ROCm-specific)
    if image_lower.contains("pytorch") && (image_lower.contains("gpu") || image_lower.contains("cuda")) {
        return Some(("pytorch".to_string(), None));
    }

    None
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_collect_stone_resources() {
        let resources = collect_stone_resources().unwrap();
        assert!(resources.cpu.cores > 0);
        assert!(resources.memory.total_bytes > 0);
        assert!(resources.disk.total_bytes > 0);
    }

    #[test]
    fn test_extract_ai_runtime_from_image() {
        // ROCm images
        assert_eq!(
            extract_ai_runtime_from_image("rocm/pytorch:rocm5.7_ubuntu22.04_py3.10"),
            Some(("rocm".to_string(), Some("5.7".to_string())))
        );

        // CUDA images
        assert_eq!(
            extract_ai_runtime_from_image("nvidia/cuda:12.2.0-base-ubuntu22.04"),
            Some(("cuda".to_string(), Some("12.2".to_string())))
        );

        // TensorFlow GPU
        assert_eq!(
            extract_ai_runtime_from_image("tensorflow/tensorflow:latest-gpu"),
            Some(("tensorflow".to_string(), None))
        );

        // Non-AI image
        assert_eq!(
            extract_ai_runtime_from_image("nginx:latest"),
            None
        );
    }
}
