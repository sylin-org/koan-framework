use anyhow::{Context, Result};
use sysinfo::System;
use std::fs;
use std::process::Command;
use garden_common::{format_bytes, format_uptime, CpuMetrics, DiskMetrics, GpuInfo, MemoryMetrics, StoneResources};

/// Collect CPU model and features from /proc/cpuinfo
pub fn get_cpu_info() -> Result<(String, Vec<String>, String)> {
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
    // Use PowerShell to query GPU via WMI
    let output = Command::new("powershell")
        .args([
            "-NoProfile",
            "-Command",
            "Get-WmiObject Win32_VideoController | Select-Object Name, AdapterRAM | ConvertTo-Json"
        ])
        .output()?;
    
    if !output.status.success() {
        anyhow::bail!("PowerShell GPU query failed");
    }
    
    let stdout = String::from_utf8_lossy(&output.stdout);
    
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
            let vram_bytes = gpu["AdapterRAM"].as_u64();
            let vram_mb = vram_bytes.map(|b| b / 1024 / 1024);
            
            // Detect vendor from name
            let name_lower = name.to_lowercase();
            let (vendor, capabilities): (&str, Vec<String>) = if name_lower.contains("nvidia") || name_lower.contains("geforce") || name_lower.contains("quadro") {
                ("NVIDIA", vec!["cuda".to_string(), "vulkan".to_string(), "directml".to_string()])
            } else if name_lower.contains("amd") || name_lower.contains("radeon") {
                ("AMD", vec!["vulkan".to_string(), "directml".to_string()])
            } else if name_lower.contains("intel") {
                ("Intel", vec!["vulkan".to_string(), "directml".to_string()])
            } else {
                ("Unknown", vec!["vulkan".to_string(), "directml".to_string()])
            };
            
            Some(GpuInfo {
                vendor: vendor.to_string(),
                model: name,
                vram_mb,
                capabilities,
            })
        })
        .collect();
        
        if !gpus.is_empty() {
            return Ok(gpus);
        }
    }
    
    anyhow::bail!("No GPUs detected via Windows API")
}

#[cfg(not(target_os = "windows"))]
#[allow(dead_code)]
fn detect_windows_gpus() -> Result<Vec<GpuInfo>> {
    anyhow::bail!("Windows GPU detection not available on this platform")
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
}
