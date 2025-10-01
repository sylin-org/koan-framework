# Migrating Koan Framework Applications to .NET 10

**Audience**: Developers migrating existing Koan Framework applications from .NET 9 to .NET 10
**Last Updated**: 2025-10-01
**Framework Version**: Koan 0.6.3+
**.NET Version**: 10.0.100-rc.1.25451.107+

## Overview

Koan Framework v0.6.3 and later fully support .NET 10 RC 1. This guide walks you through migrating your existing Koan-based applications to .NET 10.

**Migration Effort**: Low (30-60 minutes for most applications)
**Breaking Changes**: Minimal (see [Breaking Changes](#breaking-changes))
**Testing Recommended**: Full regression testing after migration

## Prerequisites

### Required Software

1. **.NET 10 SDK** (RC 1 or later)
   - Download: https://dotnet.microsoft.com/download/dotnet/10.0
   - Verify: `dotnet --version` should show `10.0.100-rc.1` or later

2. **Updated Development Tools** (if applicable)
   - Visual Studio 2022 17.12+ (for .NET 10 support)
   - Rider 2024.3+ (for .NET 10 support)
   - VS Code + C# extension (latest)

3. **Docker** (for containerized applications)
   - Ensure Docker Desktop or Docker CE is updated
   - Test: `docker pull mcr.microsoft.com/dotnet/sdk:10.0.100-rc.1`

## Migration Steps

### Step 1: Update global.json

If your project uses `global.json`, update the SDK version:

```json
{
  "sdk": {
    "version": "10.0.100-rc.1.25451.107",
    "rollForward": "latestFeature",
    "allowPrerelease": true
  }
}
```

**Why**: Pins the SDK version for consistent builds across your team.

### Step 2: Update Target Framework

Update all `.csproj` files to target `net10.0`:

```xml
<!-- Before -->
<TargetFramework>net9.0</TargetFramework>

<!-- After -->
<TargetFramework>net10.0</TargetFramework>
```

**Bulk Update** (Windows PowerShell):
```powershell
Get-ChildItem -Recurse -Filter *.csproj | ForEach-Object {
    (Get-Content $_.FullName) -replace '<TargetFramework>net9\.0</TargetFramework>', '<TargetFramework>net10.0</TargetFramework>' |
    Set-Content $_.FullName
}
```

**Bulk Update** (Linux/macOS):
```bash
find . -name "*.csproj" -type f -exec sed -i 's/<TargetFramework>net9\.0<\/TargetFramework>/<TargetFramework>net10.0<\/TargetFramework>/g' {} +
```

### Step 3: Update NuGet Packages

#### Update Koan Framework Packages

Koan Framework 0.6.3+ supports .NET 10. Update all Koan packages:

```bash
# Update all Koan.* packages to latest
dotnet list package | grep Koan | ForEach-Object {
    $package = ($_ -split '\s+')[1]
    dotnet add package $package
}
```

Or update individually:
```bash
dotnet add package Koan.Core
dotnet add package Koan.Web
dotnet add package Koan.Data.Core
# ... etc
```

#### Update Microsoft.Extensions Packages

Update all Microsoft.Extensions.* packages to .NET 10 RC 1 versions:

```bash
dotnet add package Microsoft.Extensions.DependencyInjection --version 10.0.0-rc.1.25451.107
dotnet add package Microsoft.Extensions.Configuration --version 10.0.0-rc.1.25451.107
dotnet add package Microsoft.Extensions.Logging --version 10.0.0-rc.1.25451.107
# ... etc
```

#### Remove System.Linq.Async (if present)

.NET 10 includes `System.Linq.Async` functionality built-in:

```bash
dotnet remove package System.Linq.Async
```

If you have namespace conflicts, add:
```csharp
global using System.Linq;
```

### Step 4: Update Docker Configuration

If you use Docker, update `Dockerfile` images:

```dockerfile
# Before
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
FROM mcr.microsoft.com/dotnet/aspnet:9.0

# After
FROM mcr.microsoft.com/dotnet/sdk:10.0.100-rc.1 AS build
FROM mcr.microsoft.com/dotnet/aspnet:10.0
```

**Note**: SDK uses full version tag (`10.0.100-rc.1`), runtime uses short tag (`10.0`).

### Step 5: Address Breaking Changes

#### WebHostBuilder → WebApplication

.NET 10 deprecates `WebHostBuilder`. Migrate to `WebApplication` pattern:

```csharp
// Before (.NET 9)
var builder = new WebHostBuilder()
    .UseEnvironment("Development")
    .UseTestServer()
    .ConfigureServices(services => {
        services.AddMvc();
    })
    .Configure(app => {
        app.UseRouting();
        app.UseEndpoints(e => e.MapControllers());
    });

// After (.NET 10)
var builder = WebApplication.CreateBuilder();
builder.Environment.EnvironmentName = "Development";
builder.WebHost.UseTestServer();

builder.Services.AddMvc();

var app = builder.Build();
app.UseRouting();
app.MapControllers();
await app.StartAsync();
```

#### Cookie Authentication

Verify cookie authentication returns 401/403 for API requests (not redirects):

```csharp
// Koan Framework handles this automatically in Koan.Web.Auth
// No changes needed if using builder.Services.AddKoan()
```

**Manual configuration** (if not using Koan.Web.Auth):
```csharp
services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o => {
        o.Events = new CookieAuthenticationEvents {
            OnRedirectToLogin = ctx => {
                if (ctx.Request.Path.StartsWithSegments("/api")) {
                    ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                }
                ctx.Response.Redirect(ctx.RedirectUri);
                return Task.CompletedTask;
            }
        };
    });
```

### Step 6: Build and Test

1. **Clean and restore**:
   ```bash
   dotnet clean
   dotnet restore
   ```

2. **Build solution**:
   ```bash
   dotnet build
   ```

3. **Run tests**:
   ```bash
   dotnet test
   ```

4. **Verify application starts**:
   ```bash
   dotnet run
   ```

### Step 7: Verify Koan Framework Patterns

Ensure core Koan patterns work correctly:

#### Entity<T> Patterns
```csharp
var todo = new Todo { Title = "Test .NET 10" };
await todo.Save();  // Should succeed

var loaded = await Todo.Get(todo.Id);  // Should retrieve
var all = await Todo.All();  // Should query
```

#### Auto-Registration
```csharp
// Check boot logs for module discovery
[K:BOOT] Koan Bootstrap
█ Runtime       : Koan.Core 0.6.3.0
█ Modules       : [count]
```

#### Multi-Provider Transparency
```csharp
// Verify data provider connectivity
var capabilities = Data<Todo, string>.QueryCaps;
// Should show provider capabilities
```

## Breaking Changes

### Removed/Deprecated APIs

1. **WebHostBuilder** (ASP.NET Core)
   - **Status**: Deprecated in .NET 10
   - **Action**: Migrate to `WebApplication.CreateBuilder()`
   - **Impact**: Test infrastructure may need updates

2. **System.Linq.Async Package**
   - **Status**: Functionality built into .NET 10
   - **Action**: Remove package reference
   - **Impact**: Namespace conflicts possible (use `global using System.Linq;`)

### Package Version Requirements

| Package Family | Minimum Version |
|----------------|-----------------|
| Koan.* | 0.6.3+ |
| Microsoft.Extensions.* | 10.0.0-rc.1+ |
| MongoDB.Driver | 2.30.0+ |
| Npgsql | 8.0+ |
| Microsoft.Data.Sqlite | 10.0.0-rc.1+ |

## Validation Checklist

Use this checklist to verify your migration:

- [ ] .NET 10 SDK installed (`dotnet --version` shows 10.0.100+)
- [ ] `global.json` updated (if present)
- [ ] All `.csproj` files use `<TargetFramework>net10.0</TargetFramework>`
- [ ] Koan Framework packages updated to 0.6.3+
- [ ] Microsoft.Extensions packages updated to 10.0.0-rc.1+
- [ ] `System.Linq.Async` removed (if present)
- [ ] Dockerfiles updated (if applicable)
- [ ] `WebHostBuilder` migrated to `WebApplication` (if used)
- [ ] Cookie authentication verified (APIs return 401/403, not redirects)
- [ ] Solution builds successfully (`dotnet build`)
- [ ] All tests pass (`dotnet test`)
- [ ] Application starts and serves requests (`dotnet run`)
- [ ] Entity<T> operations work (Save, Get, All)
- [ ] Auto-registration discovers modules (check boot logs)
- [ ] Data provider connectivity confirmed
- [ ] API endpoints respond correctly
- [ ] Docker containers build and run (if applicable)

## Troubleshooting

### Issue: Build Errors After Migration

**Symptoms**:
```
error NU1202: Package X is not compatible with net10.0
```

**Solutions**:
1. Update the incompatible package to a .NET 10-compatible version
2. Check package documentation for .NET 10 support
3. If no compatible version exists, consider alternatives or wait for update

### Issue: Docker Build Fails

**Symptoms**:
```
failed to resolve source metadata for mcr.microsoft.com/dotnet/sdk:10.0-rc: not found
```

**Solutions**:
- Use full SDK tag: `mcr.microsoft.com/dotnet/sdk:10.0.100-rc.1`
- Use short runtime tag: `mcr.microsoft.com/dotnet/aspnet:10.0`
- Pull images manually: `docker pull mcr.microsoft.com/dotnet/sdk:10.0.100-rc.1`

### Issue: Tests Fail After Migration

**Symptoms**:
- Tests using `WebHostBuilder` fail
- Partition name validation errors
- Provider not found errors

**Solutions**:
1. **WebHostBuilder failures**: Migrate to `WebApplication.CreateBuilder()`
2. **Partition validation**: Ensure partition names start with letters (not numbers)
3. **Provider issues**: Verify `builder.Services.AddKoan()` is called before provider setup

### Issue: Runtime Errors with Async/Await

**Symptoms**:
```
Ambiguous match found for 'ToAsyncEnumerable'
```

**Solutions**:
- Remove `System.Linq.Async` package
- Add `global using System.Linq;` to `GlobalUsings.cs`
- Rebuild solution

## Performance Considerations

.NET 10 includes several performance improvements:

1. **JIT Optimizations**: 5-15% faster startup in many scenarios
2. **GC Improvements**: Better memory efficiency for containerized apps
3. **HTTP Performance**: Improved Kestrel throughput

**Expected Results**:
- Comparable or better performance vs .NET 9
- No significant regressions observed in Koan Framework testing

## Rolling Back

If you need to revert to .NET 9:

1. Restore `global.json` to .NET 9 SDK version
2. Revert all `.csproj` files to `<TargetFramework>net9.0</TargetFramework>`
3. Downgrade Koan packages to 0.6.2 (last .NET 9 version)
4. Downgrade Microsoft.Extensions packages to 9.0.x
5. Re-add `System.Linq.Async` if needed
6. Restore Dockerfiles to use `9.0` images

## Support Resources

- **Framework Documentation**: [docs/index.md](../index.md)
- **Migration Reports**: [docs/migration/](./README.md)
- **Troubleshooting Guide**: [docs/support/troubleshooting.md](../support/troubleshooting.md)
- **GitHub Issues**: [Report migration issues](https://github.com/sylin-org/koan-framework/issues)

## Validation Testing

The Koan Framework team validated .NET 10 RC 1 through:

- ✅ 142 projects migrated and building successfully
- ✅ 69/89 unit tests passing (20 pre-existing failures, not regressions)
- ✅ S5.Recs sample application fully operational
- ✅ All core framework patterns validated (Entity<T>, auto-registration, multi-provider)
- ✅ Docker deployment confirmed working

**Full validation reports**: See [PHASE-4-VALIDATION-REPORT.md](./PHASE-4-VALIDATION-REPORT.md) and [PHASE-5-S5-RECS-TEST-RESULTS.md](./PHASE-5-S5-RECS-TEST-RESULTS.md)

## Timeline Guidance

### Development Phase (Now - .NET 10 GA)

- Safe to migrate internal/development projects
- Thorough testing recommended
- Prepare deployment pipelines

### Production Phase (.NET 10 GA + 1 month)

- Migrate production applications
- Monitor for issues
- Validate third-party package compatibility

### Long-term (.NET 10 GA + 6 months)

- .NET 9 reaches end of support approximately 6 months after .NET 10 GA
- Plan migration completion before .NET 9 EOL

## Summary

Migrating Koan Framework applications to .NET 10 is straightforward:

1. **Update SDK and global.json** - Set .NET 10 as baseline
2. **Change TargetFramework to net10.0** - Bulk update all projects
3. **Update packages** - Koan 0.6.3+, Microsoft.Extensions 10.0+
4. **Update Docker images** - SDK: 10.0.100-rc.1, Runtime: 10.0
5. **Address breaking changes** - WebHostBuilder → WebApplication
6. **Test thoroughly** - Build, unit tests, integration tests

**Estimated effort**: 30-60 minutes for typical applications.

**Risk level**: Low - minimal breaking changes, excellent backward compatibility.

---

**Document Version**: 1.0
**Last Updated**: 2025-10-01
**Validated Against**: .NET 10.0.100-rc.1.25451.107
**Framework Version**: Koan 0.6.3
