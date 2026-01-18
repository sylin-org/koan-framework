# Stone Root Filesystem

This directory structure mirrors the target Stone filesystem. During USB installer creation, files are copied here, then the entire directory is copied to `/target/` during Debian installation.

## Directory Structure

```
stone-root/
├── usr/
│   └── local/
│       └── bin/
│           ├── garden-moss          # Garden-moss daemon binary
│           ├── garden-lantern      # Garden-lantern service registry
│           └── garden-rake       # Garden-rake CLI binary
├── etc/
│   ├── zen-garden/
│   │   └── garden-moss.toml             # Garden-moss configuration
│   └── systemd/
│       └── system/
│           └── garden-moss.service      # Garden-moss systemd service
└── home/
    └── stone/                     # User home directory (username template)
        ├── garden-rake-quickstart.sh  # Quick reference guide
        └── garden-moss-preinstall.json       # Optional: Pre-install manifest
```

## How It Works

1. **Build Phase**: `NewStone.ps1` populates this directory with:
   - Binaries from `../bin/` (garden-moss, garden-lantern, garden-rake)
   - Generated config files (garden-moss.toml, garden-moss.service)
   - Templates (quickstart guide)
   - Optional manifest (garden-moss-preinstall.json)

2. **Copy to USB**: Entire `stone-root/` directory is copied to USB

3. **Installation**: Debian preseed runs:
   ```bash
   cp -r /cdrom/stone-root/* /target/
   ```

4. **Post-Copy**: Preseed sets permissions, ownership, enables services

## Benefits

- **Visual clarity**: Directory structure shows exactly where files land
- **Easy maintenance**: Add new files by placing them in the right directory
- **Simple deployment**: Single recursive copy instead of multiple commands
- **Template-friendly**: Can use placeholders in filenames (e.g., `home/{{USERNAME}}/`)

## File Permissions

Set during preseed late_command:
- Binaries: `chmod +x /usr/local/bin/*`
- User files: `chown -R stone:stone /home/stone`
- Service files: Handled by systemd
