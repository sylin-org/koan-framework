# How to Restart Koan.Context Service

## The Problem
After rebuilding the UI (`npm run build`), the browser still shows old content because:
1. The backend service caches static files in memory
2. The browser caches the old JavaScript/CSS files

## The Solution

### Step 1: Stop the Service
```bash
# Find and kill the process
taskkill /F /IM dotnet.exe /T
# OR if started with start.bat:
# Press Ctrl+C in the terminal running the service
```

### Step 2: Start the Service
```bash
cd F:\Replica\NAS\Files\repo\github\koan-framework\src\Services\code-intelligence\Koan.Service.KoanContext
start.bat
```

### Step 3: Hard Refresh Browser
- **Windows:** Press `Ctrl + F5` or `Ctrl + Shift + R`
- **Mac:** Press `Cmd + Shift + R`
- **Manual:** Open DevTools (F12) → Right-click refresh button → "Empty Cache and Hard Reload"

## Quick One-Liner (From Koan.Context directory)
```bash
taskkill /F /IM dotnet.exe /T ; start.bat
```

Then refresh browser with `Ctrl + F5`
