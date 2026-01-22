//! Platform-specific utility functions
//!
//! Provides OS-specific operations:
//! - Drive type detection (Windows)
//! - Removable media detection
//! - Platform-specific file paths

/// Check if executable is running from removable media (USB, flash drive, etc.)
///
/// # Platform Support
/// - **Windows**: Uses GetDriveTypeW to detect removable/CD-ROM drives
/// - **Linux/Mac**: Always returns false (not applicable)
///
/// # Returns
/// - `Ok(true)`: Running from removable media (USB/CD-ROM)
/// - `Ok(false)`: Running from fixed drive
/// - `Err(_)`: Failed to detect drive type
///
/// # Example
/// ```rust,no_run
/// use std::env;
/// use garden_moss::infra::platform::is_running_from_removable_media;
///
/// # fn main() -> anyhow::Result<()> {
/// let exe_path = env::current_exe()?;
/// if is_running_from_removable_media(&exe_path)? {
///     println!("Running from USB drive - will copy to permanent location");
/// }
/// # Ok(())
/// # }
/// ```
#[cfg(target_os = "windows")]
pub fn is_running_from_removable_media(exe_path: &std::path::Path) -> anyhow::Result<bool> {
    use windows_sys::Win32::Storage::FileSystem::GetDriveTypeW;
    use std::os::windows::ffi::OsStrExt;

    // Get drive letter (e.g., "C:\")
    let drive = exe_path
        .components()
        .next()
        .and_then(|c| c.as_os_str().to_str())
        .ok_or_else(|| anyhow::anyhow!("Failed to extract drive letter"))?;

    // Format as root path (e.g., "C:\")
    let root_path = format!("{}\\", drive);

    // Convert to wide string for Windows API
    let wide: Vec<u16> = std::ffi::OsStr::new(&root_path)
        .encode_wide()
        .chain(std::iter::once(0))
        .collect();

    // Call GetDriveTypeW
    let drive_type = unsafe { GetDriveTypeW(wide.as_ptr()) };

    // DRIVE_REMOVABLE = 2, DRIVE_CDROM = 5
    // Consider both as "removable" for our purposes
    Ok(drive_type == 2 || drive_type == 5)
}

/// Non-Windows platforms: always return false (not applicable)
#[cfg(not(target_os = "windows"))]
pub fn is_running_from_removable_media(_exe_path: &std::path::Path) -> anyhow::Result<bool> {
    Ok(false)
}
