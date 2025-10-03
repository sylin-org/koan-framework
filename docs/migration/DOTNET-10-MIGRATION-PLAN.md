# Koan Framework: .NET 10 RC 1 Migration Plan

## Executive Summary

**Objective**: Migrate Koan Framework from .NET 9.0 to .NET 10 RC 1

**Timeline**: 2-3 weeks (18-27 working days)

**Risk Level**: Medium-High

**Branch**: `feature/dotnet-10-rc1-migration`

**Go/No-Go Decision Date**: After Phase 1 completion

---

## Migration Scope

### Projects Affected
- **Core Framework**: 15 projects
- **Data Connectors**: 12 projects
- **Specialized Modules**: 10 projects
- **Sample Applications**: 20 projects
- **Test Projects**: 40+ projects
- **Total**: 100+ .csproj files

### Infrastructure Affected
- 22 Dockerfile configurations
- Build scripts and tooling
- CI/CD pipelines (when re-enabled)
- Developer environments

---

## Phase 1: Foundation & Preparation

**Duration**: 2-3 days
**Risk**: Low
**Prerequisites**: .NET 10 RC 1 SDK installed (see [DOTNET-10-RC1-INSTALLATION.md](./DOTNET-10-RC1-INSTALLATION.md))

### 1.1 Create Global SDK Configuration

**Task**: Pin SDK version for consistent builds

```bash
# Create global.json in repository root
cat > global.json << 'EOF'
{
  "sdk": {
    "version": "10.0.100-rc.1.25451.107",
    "rollForward": "latestMinor",
    "allowPrerelease": true
  }
}
EOF
```

**Verification**:
```bash
dotnet --version
# Expected: 10.0.100-rc.1.25451.107
```

### 1.2 Update All Project Target Frameworks

**Task**: Change all `.csproj` files from `net9.0` to `net10.0`

**Automated Approach**:

```powershell
# PowerShell script (Windows)
Get-ChildItem -Path . -Recurse -Filter *.csproj | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $updated = $content -replace '<TargetFramework>net9\.0</TargetFramework>', '<TargetFramework>net10.0</TargetFramework>'
    Set-Content -Path $_.FullName -Value $updated -NoNewline
}
```

```bash
# Bash script (Linux/macOS)
find . -name "*.csproj" -type f -exec sed -i 's/<TargetFramework>net9\.0<\/TargetFramework>/<TargetFramework>net10.0<\/TargetFramework>/g' {} +
```

**Manual Review Required**:
- Verify changes don't affect multi-targeted projects
- Review any projects with custom target framework logic

**Files to Update** (~100 files):
- `src/**/*.csproj`
- `samples/**/*.csproj`
- `tests/**/*.csproj`
- `templates/**/*.csproj`

**Verification**:
```bash
# Check for any remaining net9.0 references
grep -r "TargetFramework>net9.0<" --include="*.csproj" .

# Should return no results
```

### 1.3 Update Directory.Build.props

**Task**: Ensure build properties support .NET 10

**File**: `Directory.Build.props`

**No changes needed** - current settings are version-agnostic:
```xml
<LangVersion Condition="'$(LangVersion)' == ''">latestMajor</LangVersion>
```

**Verification**:
```bash
# Build properties should work with net10.0
dotnet build --verbosity minimal
```

### 1.4 Initial Build Test

**Task**: Attempt initial build to identify immediate issues

```bash
# Clean all previous builds
dotnet clean

# Restore packages
dotnet restore

# Build entire solution
dotnet build --configuration Release

# Capture build output
dotnet build > build-phase1.log 2>&1
```

**Expected Issues**:
- Package version mismatches (expected - will fix in Phase 2)
- Missing .NET 10 compatible packages
- API compatibility warnings

**Action**: Document all errors/warnings for Phase 2 remediation

**Success Criteria**:
- ✅ All .csproj files updated to `net10.0`
- ✅ `global.json` created and verified
- ✅ Initial build attempted (may fail - that's OK)
- ✅ Build log captured for analysis

---

## Phase 2: Package Dependency Updates

**Duration**: 3-5 days
**Risk**: Medium

### 2.1 Update Microsoft.Extensions.* Packages

**Target Version**: `10.0.0-rc.1.25451.107`

**Packages to Update** (70+ references):

```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="10.0.0-rc.1.25451.107" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.0-rc.1.25451.107" />
<PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" Version="10.0.0-rc.1.25451.107" />
<PackageReference Include="Microsoft.Extensions.Configuration" Version="10.0.0-rc.1.25451.107" />
<PackageReference Include="Microsoft.Extensions.Configuration.Binder" Version="10.0.0-rc.1.25451.107" />
<PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="10.0.0-rc.1.25451.107" />
<PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="10.0.0-rc.1.25451.107" />
<PackageReference Include="Microsoft.Extensions.Options" Version="10.0.0-rc.1.25451.107" />
<PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" Version="10.0.0-rc.1.25451.107" />
<PackageReference Include="Microsoft.Extensions.Options.DataAnnotations" Version="10.0.0-rc.1.25451.107" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="10.0.0-rc.1.25451.107" />
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0-rc.1.25451.107" />
<PackageReference Include="Microsoft.Extensions.Logging.Console" Version="10.0.0-rc.1.25451.107" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.0-rc.1.25451.107" />
<PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" Version="10.0.0-rc.1.25451.107" />
<PackageReference Include="Microsoft.Extensions.Http" Version="10.0.0-rc.1.25451.107" />
<PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="10.0.0-rc.1.25451.107" />
<PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks" Version="10.0.0-rc.1.25451.107" />
<PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore" Version="10.0.0-rc.1.25451.107" />
```

**Automated Update**:

```bash
# Use find/replace or script for bulk update
# PowerShell example:
Get-ChildItem -Path . -Recurse -Filter *.csproj | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    $updated = $content -replace 'Microsoft\.Extensions\.([^"]+)" Version="9\.0\.\d+"', 'Microsoft.Extensions.$1" Version="10.0.0-rc.1.25451.107"'
    Set-Content -Path $_.FullName -Value $updated -NoNewline
}
```

**Verification**:
```bash
# Check for any remaining 9.0.x versions
grep -r "Microsoft.Extensions.*Version=\"9\.0" --include="*.csproj" .

# Should return no results
```

### 2.2 Update ASP.NET Core Packages

**Target Version**: `10.0.0-rc.1.25451.107`

**Files to Update**:
- `src/Koan.Web/Koan.Web.csproj`

```xml
<PackageReference Include="Microsoft.AspNetCore.Mvc.NewtonsoftJson" Version="10.0.0-rc.1.25451.107" />
```

**Note**: `FrameworkReference` for `Microsoft.AspNetCore.App` is version-agnostic (no change needed)

### 2.3 Update Database Provider Packages

#### PostgreSQL (Npgsql)

**Current**: `9.0.3`
**Target**: `10.0.0` or latest compatible RC version

**Research Required**:
```bash
# Check available Npgsql versions
dotnet list package --include-prerelease | grep Npgsql

# Or check NuGet.org
# https://www.nuget.org/packages/Npgsql
```

**Files to Update**:
- `src/Connectors/Data/Postgres/Koan.Data.Connector.Postgres.csproj`
- `tests/Koan.Data.Core.Tests/Koan.Data.Core.Tests.csproj`
- `tests/Koan.Data.Connector.Postgres.Tests/Koan.Data.Connector.Postgres.Tests.csproj`

**Action**: Update to latest .NET 10 compatible version once identified

#### MongoDB

**Current**: `3.5.0`
**Action**: Verify compatibility with .NET 10

**Files to Check**:
- `src/Connectors/Data/Mongo/Koan.Data.Connector.Mongo.csproj`
- `src/Connectors/Data/Cqrs/Outbox/Mongo/Koan.Data.Cqrs.Outbox.Connector.Mongo.csproj`
- `tests/Koan.Data.Core.Tests/Koan.Data.Core.Tests.csproj`

**Expected**: MongoDB.Driver 3.5.0 should be compatible (no update needed unless issues arise)

#### SQLite

**Current**: `9.0.9`
**Target**: `10.0.0-rc.1.25451.107`

**Files to Update**:
- `src/Connectors/Data/Sqlite/Koan.Data.Connector.Sqlite.csproj`
- Multiple test projects using SQLite

```xml
<PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.0-rc.1.25451.107" />
```

### 2.4 Update .NET Aspire Packages

**Current**: Mixed (`9.0.0` and `9.5.0`)
**Target**: `10.0.0-rc.1.*` (check availability)

**Research Required**:
```bash
# Check Aspire RC versions
dotnet list package --include-prerelease | grep Aspire
```

**Files to Update**:

`samples/KoanAspireIntegration.AppHost/KoanAspireIntegration.AppHost.csproj`:
```xml
<Sdk Name="Aspire.AppHost.Sdk" Version="10.0.0-rc.1" />
<PackageReference Include="Aspire.Hosting.AppHost" Version="10.0.0-rc.1.xxx" />
```

`src/Koan.Orchestration.Aspire/Koan.Orchestration.Aspire.csproj`:
```xml
<PackageReference Include="Aspire.Hosting" Version="10.0.0-rc.1.xxx" />
```

`src/Connectors/Data/Postgres/Koan.Data.Connector.Postgres.csproj`:
```xml
<PackageReference Include="Aspire.Hosting.PostgreSQL" Version="10.0.0-rc.1.xxx" />
```

`src/Connectors/Data/Redis/Koan.Data.Connector.Redis.csproj`:
```xml
<PackageReference Include="Aspire.Hosting.Redis" Version="10.0.0-rc.1.xxx" />
```

### 2.5 Update Test Framework Packages

**Standardize all test projects to latest versions**

**Target Versions**:
- `xunit`: `2.9.3` (latest stable)
- `xunit.runner.visualstudio`: `3.1.5` (latest stable)
- `Microsoft.NET.Test.Sdk`: `17.12.0` (or latest .NET 10 compatible)

**Files to Update**:
- `tests/S13.DocMind.UnitTests/S13.DocMind.UnitTests.csproj` (currently 2.5.1)
- `tests/S13.DocMind.IntegrationTests/S13.DocMind.IntegrationTests.csproj` (currently 2.5.1)

```xml
<PackageReference Include="xunit" Version="2.9.3" />
<PackageReference Include="xunit.runner.visualstudio" Version="3.1.5">
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
<PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
```

### 2.6 Verify OpenTelemetry Compatibility

**Current**: `1.12.0`
**Action**: Verify .NET 10 compatibility

**Packages**:
- OpenTelemetry.Extensions.Hosting
- OpenTelemetry.Exporter.OpenTelemetryProtocol
- OpenTelemetry.Instrumentation.AspNetCore
- OpenTelemetry.Instrumentation.Http
- OpenTelemetry.Instrumentation.Runtime

**Expected**: Version 1.12.0 should be compatible (no update needed unless issues arise)

**Verification Test**:
```bash
# Build project with OpenTelemetry
dotnet build src/Koan.Core/Koan.Core.csproj

# Check for compatibility warnings
```

### 2.7 Third-Party Package Compatibility Check

**Packages to Verify**:

| Package | Current Version | .NET 10 Status | Action |
|---------|----------------|----------------|--------|
| Dapper | 2.1.66 | ✅ Likely compatible | Test |
| Newtonsoft.Json | 13.0.4 | ✅ Mature, stable | No change |
| PdfPig | 0.1.9 | ⚠️ Verify | Test or update |
| DocumentFormat.OpenXml | 3.0.1 | ⚠️ Verify | Test or update |
| SixLabors.ImageSharp | 3.1.11 | ✅ Recently updated | No change |

**Action**:
```bash
# Test build after all updates
dotnet restore
dotnet build --configuration Release

# Check for warnings
grep -i "warning" build.log
```

### 2.8 Build Verification

**After all package updates**:

```bash
# Clean build
dotnet clean
rm -rf */bin */obj

# Restore with verbose logging
dotnet restore -v detailed > restore-phase2.log 2>&1

# Build all projects
dotnet build --configuration Release > build-phase2.log 2>&1

# Check for errors
grep -i "error" build-phase2.log

# Check for warnings
grep -i "warning" build-phase2.log
```

**Success Criteria**:
- ✅ All Microsoft packages updated to 10.0.0-rc.1.*
- ✅ Database providers updated or compatibility verified
- ✅ Test frameworks standardized
- ✅ Build completes without package version errors
- ✅ Only expected warnings (breaking changes we'll address in Phase 3)

---

## Phase 3: Breaking Changes Remediation

**Duration**: 3-4 days
**Risk**: Medium-High

### 3.1 ASP.NET Core - Cookie Login Redirects

**Breaking Change**: Cookie authentication no longer redirects for API endpoints (returns 401/403 instead)

**Affected Areas**:
- `src/Koan.Web.Auth/`
- `samples/S6.Auth/`
- `samples/S6.SocialCreator/`

**Search for Affected Code**:
```bash
# Find cookie authentication configuration
grep -r "AddCookie\|CookieAuthenticationDefaults" --include="*.cs" src/Koan.Web.Auth/

# Find API endpoints with authentication
grep -r "\[Authorize\]" --include="*.cs" samples/S6*/
```

**Remediation**:

Review authentication configuration in affected files. Ensure API endpoints properly handle 401/403 responses instead of expecting redirects.

**Example Fix** (if needed):
```csharp
// Before (.NET 9)
services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login"; // Redirects everywhere
    });

// After (.NET 10)
services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.Events.OnRedirectToLogin = context =>
        {
            // Return 401 for API requests instead of redirecting
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });
```

**Testing**:
```bash
# Run auth-related tests
dotnet test tests/Koan.Web.Auth.Tests/ -v normal

# Run S6 sample tests
dotnet test tests/S6.SocialCreator.IntegrationTests/ -v normal
```

### 3.2 ASP.NET Core - Obsolete WebHostBuilder

**Breaking Change**: `WebHostBuilder`, `IWebHost`, and `WebHost` are obsolete

**Search for Usage**:
```bash
# Find WebHostBuilder usage
grep -r "WebHostBuilder\|IWebHost\|WebHost" --include="*.cs" samples/ src/

# Expected: Should find minimal or no usage (modern apps use WebApplicationBuilder)
```

**Action**:
- If found, migrate to `WebApplicationBuilder` pattern
- Most Koan samples should already use modern hosting model

**Example Migration** (if needed):
```csharp
// Old pattern (.NET 9 and earlier)
public class Program
{
    public static void Main(string[] args)
    {
        CreateWebHostBuilder(args).Build().Run();
    }

    public static IWebHostBuilder CreateWebHostBuilder(string[] args) =>
        WebHost.CreateDefaultBuilder(args)
            .UseStartup<Startup>();
}

// New pattern (.NET 10)
var builder = WebApplication.CreateBuilder(args);

// Configure services
builder.Services.AddKoan();
builder.Services.AddControllers();

var app = builder.Build();

// Configure middleware
app.UseRouting();
app.MapControllers();

app.Run();
```

### 3.3 ASP.NET Core - Razor Runtime Compilation Obsolete

**Search for Usage**:
```bash
# Find AddRazorRuntimeCompilation usage
grep -r "AddRazorRuntimeCompilation" --include="*.cs" samples/ src/
```

**Likely Affected Samples**:
- S7.TechDocs (content platform)
- S10.DevPortal (developer portal)

**Remediation**:
- Remove `AddRazorRuntimeCompilation()` calls
- Use precompiled Razor views
- For development, configure view compilation in .csproj:

```xml
<PropertyGroup>
  <PreserveCompilationContext>true</PreserveCompilationContext>
</PropertyGroup>
```

### 3.4 ASP.NET Core - WithOpenApi Deprecated

**Affected Area**: `src/Connectors/Web/Swagger/Koan.Web.Connector.Swagger.csproj`

**Search for Usage**:
```bash
grep -r "WithOpenApi" --include="*.cs" src/Connectors/Web/Swagger/
```

**Remediation**:
Replace `WithOpenApi()` with direct OpenAPI configuration:

```csharp
// Before
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen().WithOpenApi();

// After
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Koan API",
        Version = "v1"
    });
});
```

### 3.5 IActionContextAccessor Obsolete

**Search for Usage**:
```bash
grep -r "IActionContextAccessor\|ActionContextAccessor" --include="*.cs" src/ samples/
```

**Remediation** (if found):
Use `IHttpContextAccessor` instead:

```csharp
// Before
private readonly IActionContextAccessor _accessor;
var actionContext = _accessor.ActionContext;

// After
private readonly IHttpContextAccessor _httpContextAccessor;
var httpContext = _httpContextAccessor.HttpContext;
```

### 3.6 Entity Framework Core - SQL Server JSON Type

**Risk**: Medium-High (if using SQL Server connector)

**Affected Area**: `src/Connectors/Data/SqlServer/`

**Issue**: Primitive collections/owned types now map to `json` type instead of `nvarchar(max)`

**Action**:
1. Test SQL Server connector thoroughly
2. If database columns exist, may need migration:

```sql
-- Migration to update existing columns
ALTER TABLE MyTable
ALTER COLUMN MyJsonColumn json;
```

3. For Azure SQL Database, this feature is temporarily disabled in RC1

**Testing**:
```bash
# Run SQL Server connector tests
dotnet test tests/Koan.Data.Connector.SqlServer.Tests/ -v normal
```

### 3.7 Build and Test After Remediation

```bash
# Full clean build
dotnet clean
dotnet restore
dotnet build --configuration Release

# Run all tests
dotnet test --configuration Release --logger "console;verbosity=normal"

# Check for remaining obsolete warnings
dotnet build | grep -i "obsolete"
```

**Success Criteria**:
- ✅ No obsolete API usage warnings
- ✅ Authentication flows work correctly (401/403 instead of redirects)
- ✅ Swagger/OpenAPI integration functional
- ✅ All connector builds succeed
- ✅ Unit tests pass

---

## Phase 4: Koan Framework Pattern Validation

**Duration**: 4-6 days
**Risk**: High (Critical to framework integrity)

### 4.1 Auto-Registration System Test

**Critical Pattern**: `KoanAutoRegistrar` must work correctly with .NET 10 DI

**Test Files**:
```bash
# Find all KoanAutoRegistrar implementations
find . -name "KoanAutoRegistrar.cs" -type f

# Should find implementations in:
# - src/Koan.*/Initialization/
# - src/Connectors/*/Initialization/
```

**Test Plan**:

1. **Individual Connector Test**:
```bash
# Test each connector's auto-registration
dotnet test tests/Koan.Core.Tests/ -v normal --filter "FullyQualifiedName~AutoRegistrar"
```

2. **Integration Test**:
Create test project to verify all connectors register correctly:

```csharp
// Test: Verify all providers auto-register
[Fact]
public void AllConnectors_ShouldAutoRegister()
{
    var services = new ServiceCollection();
    services.AddKoan();

    var provider = services.BuildServiceProvider();

    // Verify data providers registered
    var dataProviders = provider.GetServices<IDataProvider>();
    Assert.NotEmpty(dataProviders);

    // Verify specific connectors
    Assert.Contains(dataProviders, p => p.Name == "postgresql");
    Assert.Contains(dataProviders, p => p.Name == "mongodb");
    Assert.Contains(dataProviders, p => p.Name == "sqlite");
}
```

3. **Boot Report Verification**:
```bash
# Run sample with boot report logging
dotnet run --project samples/S1.Web/ --configuration Release

# Check logs for:
# [INFO] Koan:modules data→postgresql
# [INFO] Koan:modules web→controllers
```

**Success Criteria**:
- ✅ All `KoanAutoRegistrar` implementations work
- ✅ Services registered correctly in DI container
- ✅ Provider election logic works
- ✅ Boot reports generate successfully

### 4.2 Entity Pattern Testing

**Critical Patterns**:
- `Entity<T>` with auto GUID v7 generation
- `Entity<T,K>` with custom keys
- `Todo.Get(id)`, `todo.Save()` static methods

**Test Strategy**:

1. **GUID v7 Generation Test**:
```csharp
[Fact]
public async Task Entity_ShouldAutoGenerateGuidV7()
{
    var todo = new Todo { Title = "Test" };

    // ID should be auto-generated before save
    Assert.NotEqual(Guid.Empty, Guid.Parse(todo.Id));

    // Should be GUID v7 (version bits = 7)
    var guid = Guid.Parse(todo.Id);
    var version = (guid.ToByteArray()[7] & 0xF0) >> 4;
    Assert.Equal(7, version);
}
```

2. **Entity CRUD Test**:
```csharp
[Fact]
public async Task Entity_CRUD_ShouldWork()
{
    // Create
    var todo = new Todo { Title = "Buy milk" };
    await todo.Save();
    var id = todo.Id;

    // Read
    var loaded = await Todo.Get(id);
    Assert.Equal("Buy milk", loaded.Title);

    // Update
    loaded.Title = "Buy bread";
    await loaded.Save();

    // Verify update
    var updated = await Todo.Get(id);
    Assert.Equal("Buy bread", updated.Title);

    // Delete
    await updated.Delete();
    var deleted = await Todo.Get(id);
    Assert.Null(deleted);
}
```

3. **Custom Key Entity Test**:
```csharp
[Fact]
public async Task EntityWithCustomKey_ShouldWork()
{
    var entity = new NumericEntity { Id = 42, Name = "Test" };
    await entity.Save();

    var loaded = await NumericEntity.Get(42);
    Assert.Equal("Test", loaded.Name);
}
```

**Test Execution**:
```bash
# Run entity pattern tests
dotnet test tests/Koan.Data.Core.Tests/ -v normal --filter "FullyQualifiedName~Entity"

# Run across all providers
dotnet test tests/Koan.Data.Connector.*.Tests/ -v normal
```

### 4.3 Provider Capability Detection

**Critical Pattern**: `Data<T,K>.QueryCaps` must correctly report provider capabilities

**Test Strategy**:

```csharp
[Theory]
[InlineData("postgresql", QueryCapabilities.LinqQueries)]
[InlineData("mongodb", QueryCapabilities.LinqQueries)]
[InlineData("sqlite", QueryCapabilities.LinqQueries)]
[InlineData("json", QueryCapabilities.None)]
public void QueryCaps_ShouldReportCorrectCapabilities(string provider, QueryCapabilities expected)
{
    using var context = EntityContext.With(provider);
    var capabilities = Data<Todo, string>.QueryCaps.Capabilities;

    Assert.True(capabilities.HasFlag(expected));
}
```

**Test Execution**:
```bash
# Test query capabilities
dotnet test tests/Koan.Data.Core.Tests/ -v normal --filter "FullyQualifiedName~QueryCaps"
```

### 4.4 Multi-Provider Transparency Test

**Critical Pattern**: Same entity code works across all providers

**Test Strategy**:

```csharp
[Theory]
[InlineData("postgresql")]
[InlineData("mongodb")]
[InlineData("sqlite")]
public async Task MultiProvider_SameCRUD_ShouldWork(string provider)
{
    using var context = EntityContext.With(provider);

    // Create
    var todo = new Todo { Title = $"Test {provider}" };
    await todo.Save();
    var id = todo.Id;

    // Read
    var loaded = await Todo.Get(id);
    Assert.Equal($"Test {provider}", loaded.Title);

    // Query
    var todos = await Todo.Query($"Title == 'Test {provider}'");
    Assert.Single(todos);

    // Cleanup
    await loaded.Delete();
}
```

**Test Execution**:
```bash
# Run multi-provider tests
dotnet test tests/Koan.Data.Core.Tests/ -v normal --filter "FullyQualifiedName~MultiProvider"
```

### 4.5 Relationship Navigation Test

**Test Strategy**:

```csharp
[Fact]
public async Task EntityRelationships_ShouldNavigate()
{
    var user = new User { Name = "Alice" };
    await user.Save();

    var todo = new Todo
    {
        Title = "Task",
        UserId = user.Id
    };
    await todo.Save();

    // Navigate relationship
    var relatives = await todo.GetRelatives();
    Assert.Contains(relatives, r => r is User u && u.Name == "Alice");
}
```

**Test Execution**:
```bash
# Test relationship navigation
dotnet test tests/Koan.Data.Core.Tests/ -v normal --filter "FullyQualifiedName~Relationship"
```

### 4.6 Integration Test Execution

```bash
# Run ALL data layer tests
dotnet test tests/Koan.Data.*.Tests/ -v normal --logger "console;verbosity=detailed"

# Run connector-specific integration tests
dotnet test tests/Koan.Data.Connector.Postgres.Tests/ -v normal
dotnet test tests/Koan.Data.Connector.Mongo.Tests/ -v normal
dotnet test tests/Koan.Data.Connector.Sqlite.Tests/ -v normal
dotnet test tests/Koan.Data.Connector.Redis.IntegrationTests/ -v normal

# Run vector connector tests
dotnet test tests/Koan.Data.Vector.Connector.Weaviate.IntegrationTests/ -v normal
```

**Success Criteria**:
- ✅ All entity patterns work with .NET 10
- ✅ GUID v7 generation works correctly
- ✅ Query capability detection works
- ✅ Multi-provider transparency maintained
- ✅ Relationship navigation works
- ✅ All data layer tests pass

---

## Phase 5: Sample Application Testing

**Duration**: 3-4 days
**Risk**: Medium

### 5.1 Console Samples

**S0.ConsoleJsonRepo**:
```bash
cd samples/S0.ConsoleJsonRepo
dotnet run --configuration Release

# Verify JSON repository operations work
# Check console output for errors
```

### 5.2 Web Samples

**S1.Web** (Basic Web App):
```bash
cd samples/S1.Web
dotnet run --configuration Release

# Test in browser: http://localhost:5044
# Verify pages load, data operations work
```

**S4.Web** (Advanced Web App):
```bash
cd samples/S4.Web
dotnet run --configuration Release

# Test functionality
```

### 5.3 API Samples

**S2.Api** (REST API):
```bash
cd samples/S2.Api
dotnet run --configuration Release

# Test endpoints
curl http://localhost:5000/api/todos
```

### 5.4 Domain Samples

**S5.Recs** (Recommendations):
```bash
cd samples/S5.Recs
dotnet run --configuration Release
```

**S6.SocialCreator** (Social Platform):
```bash
cd samples/S6.SocialCreator
dotnet run --configuration Release

# Test authentication flows (critical after .NET 10 auth changes)
```

**S7.TechDocs** (Content Platform):
```bash
cd samples/S7.TechDocs
dotnet run --configuration Release

# Verify Razor views work (no runtime compilation)
```

### 5.5 Service Composition Samples

**S8.Canon** (Canonical Data):
```bash
cd samples/S8.Canon
dotnet run --configuration Release
```

**S9.Location / S8.Location** (Location Services):
```bash
cd samples/S9.Location/Api
dotnet run --configuration Release
```

### 5.6 Developer Platform

**S10.DevPortal**:
```bash
cd samples/S10.DevPortal
dotnet run --configuration Release

# Test developer portal functionality
```

### 5.7 AI/MCP Samples

**S12.MedTrials**:
```bash
cd samples/S12.MedTrials
dotnet run --configuration Release

# Test MCP integration
```

**S13.DocMind**:
```bash
cd samples/S13.DocMind
dotnet run --configuration Release

# Test AI document processing
# Test vector search functionality
```

### 5.8 Performance Benchmarks

**S14.AdapterBench**:
```bash
cd samples/S14.AdapterBench
dotnet run --configuration Release

# Run adapter performance benchmarks
# Compare .NET 9 vs .NET 10 performance
```

### 5.9 Aspire Integration

**KoanAspireIntegration**:
```bash
cd samples/KoanAspireIntegration.AppHost
dotnet run --configuration Release

# Verify Aspire orchestration works with .NET 10
```

### 5.10 Sample Test Execution

```bash
# Run integration tests for each sample
dotnet test tests/S1.Web.IntegrationTests/ -v normal
dotnet test tests/S2.Api.IntegrationTests/ -v normal
dotnet test tests/S4.Web.IntegrationTests/ -v normal
dotnet test tests/S6.SocialCreator.IntegrationTests/ -v normal
dotnet test tests/S13.DocMind.UnitTests/ -v normal
dotnet test tests/S13.DocMind.IntegrationTests/ -v normal
```

**Success Criteria**:
- ✅ All samples run without runtime errors
- ✅ UI functionality works (web samples)
- ✅ API endpoints respond correctly
- ✅ Authentication works (especially S6 samples)
- ✅ Data operations succeed
- ✅ Integration tests pass

---

## Phase 6: Container and Docker Testing

**Duration**: 2-3 days
**Risk**: Medium

### 6.1 Update All Dockerfiles

**Find all Dockerfiles**:
```bash
find . -name "Dockerfile*" -type f
```

**Update Pattern** (apply to all 22 files):

```dockerfile
# Before
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final

# After
FROM mcr.microsoft.com/dotnet/sdk:10.0-rc AS build
FROM mcr.microsoft.com/dotnet/aspnet:10.0-rc AS final
```

**Automated Update**:
```bash
# Find and replace in all Dockerfiles
find . -name "Dockerfile*" -type f -exec sed -i 's/dotnet\/sdk:9\.0/dotnet\/sdk:10.0-rc/g' {} +
find . -name "Dockerfile*" -type f -exec sed -i 's/dotnet\/aspnet:9\.0/dotnet\/aspnet:10.0-rc/g' {} +
```

### 6.2 Test Container Builds

**Sample Dockerfiles to Test**:

```bash
# S1.Web
cd samples/S1.Web
docker build -t koan-s1-web:net10 .

# S2.Api
cd samples/S2.Api
docker build -t koan-s2-api:net10 .

# S12.MedTrials
cd samples/S12.MedTrials
docker build -t koan-medtrials:net10 .

# S13.DocMind
cd samples/S13.DocMind
docker build -t koan-docmind:net10 .
```

### 6.3 Test Container Execution

```bash
# Run S1.Web container
docker run -d -p 5044:5044 --name koan-s1-web koan-s1-web:net10

# Test endpoint
curl http://localhost:5044

# Check logs
docker logs koan-s1-web

# Stop and remove
docker stop koan-s1-web
docker rm koan-s1-web
```

### 6.4 Test Docker Compose

If using Docker Compose for multi-container setups:

```bash
# Update docker-compose.yml files to use .NET 10 images
# Run compose
docker-compose up -d

# Check status
docker-compose ps

# Check logs
docker-compose logs

# Tear down
docker-compose down
```

**Success Criteria**:
- ✅ All Dockerfiles updated to .NET 10 RC images
- ✅ Container builds succeed
- ✅ Containers run without errors
- ✅ Applications accessible in containers
- ✅ Multi-container scenarios work

---

## Phase 7: Comprehensive Test Suite

**Duration**: 2-3 days
**Risk**: Medium-High

### 7.1 Full Test Suite Execution

```bash
# Run ALL tests with detailed logging
dotnet test --configuration Release \
  --logger "console;verbosity=detailed" \
  --logger "trx;LogFileName=test-results.trx" \
  --results-directory ./TestResults

# Generate test report
dotnet test --logger "html;LogFileName=test-report.html"
```

### 7.2 Test Categories

**Unit Tests** (~40 test projects):
```bash
# Core tests
dotnet test tests/Koan.Core.Tests/
dotnet test tests/Koan.Data.Core.Tests/
dotnet test tests/Koan.Web.Controllers/

# AI tests
dotnet test tests/Koan.AI.Tests/
dotnet test tests/Koan.Mcp.Tests/

# Connector tests
dotnet test tests/Koan.Data.Connector.*.Tests/
dotnet test tests/Koan.Web.Auth.Tests/
```

**Integration Tests**:
```bash
# Database integration tests
dotnet test tests/Koan.Data.Connector.Postgres.Tests/
dotnet test tests/Koan.Data.Connector.Mongo.Tests/
dotnet test tests/Koan.Data.Connector.Redis.IntegrationTests/
dotnet test tests/Koan.Data.Vector.Connector.Weaviate.IntegrationTests/

# Sample integration tests
dotnet test tests/S*.IntegrationTests/
```

**E2E Tests**:
```bash
# Orchestration E2E tests
dotnet test tests/Koan.Orchestration.E2E.Tests/
```

### 7.3 Test Failure Analysis

**Capture test failures**:
```bash
# Run tests and capture failures
dotnet test --logger "console;verbosity=normal" 2>&1 | tee test-output.log

# Extract failures
grep -A 10 "Failed!" test-output.log > test-failures.log
```

**Analyze and fix each failure**:
1. Identify root cause (.NET 10 breaking change, bug, test issue)
2. Apply fix or update test
3. Re-run specific test: `dotnet test --filter "FullyQualifiedName~TestName"`
4. Repeat until all pass

### 7.4 Performance Regression Testing

**Benchmark Key Operations**:

```bash
# Run performance benchmarks
cd samples/S14.AdapterBench
dotnet run --configuration Release -- --benchmark

# Compare with .NET 9 baseline
# Document any performance regressions
```

**Test Areas**:
- Entity CRUD operations
- Query performance
- Provider switching overhead
- Auto-registration time
- Application startup time

### 7.5 Test Coverage Analysis

```bash
# Install coverage tool
dotnet tool install --global dotnet-coverage

# Run tests with coverage
dotnet-coverage collect 'dotnet test' -f xml -o coverage.xml

# Generate HTML report
dotnet tool install --global dotnet-reportgenerator-globaltool
reportgenerator -reports:coverage.xml -targetdir:coveragereport -reporttypes:Html
```

**Success Criteria**:
- ✅ All unit tests pass (100% pass rate)
- ✅ All integration tests pass
- ✅ E2E tests pass
- ✅ No critical performance regressions (< 10% slowdown acceptable for RC)
- ✅ Test coverage maintained or improved

---

## Phase 8: Documentation and Release Preparation

**Duration**: 1-2 days
**Risk**: Low

### 8.1 Update Framework Documentation

**Files to Update**:

1. **README.md** (root):
```markdown
# Koan Framework

**Now supporting .NET 10!**

## Requirements
- .NET 10 RC 1 or later
- ...
```

2. **CHANGELOG.md**:
```markdown
# Changelog

## [0.7.0] - 2025-10-XX

### Changed
- Migrated to .NET 10 RC 1
- Updated all dependencies to .NET 10 compatible versions
- Updated Dockerfiles to .NET 10 base images

### Breaking Changes
- Cookie authentication now returns 401/403 for API endpoints instead of redirects
- Removed obsolete WebHostBuilder pattern
- Updated Swagger integration (removed WithOpenApi)

### Fixed
- Fixed compatibility issues with .NET 10 DI container
- Addressed Entity Framework Core JSON type changes
```

3. **docs/migration/MIGRATION-FROM-NET9.md**:
Create user migration guide for projects using Koan Framework

### 8.2 Update Sample Documentation

**Update each sample README**:

```markdown
# S1.Web Sample

## Requirements
- .NET 10 RC 1 SDK or later
- ...

## Running
dotnet run

# Or with Docker
docker build -t s1-web .
docker run -p 5044:5044 s1-web
```

### 8.3 Update Package Metadata

**Update all .csproj package versions**:

```xml
<PropertyGroup>
  <Version>0.7.0-rc.1</Version>
  <AssemblyVersion>0.7.0</AssemblyVersion>
  <FileVersion>0.7.0</FileVersion>
  <PackageReleaseNotes>
    - .NET 10 RC 1 support
    - Breaking changes: See CHANGELOG.md
    - Updated dependencies
  </PackageReleaseNotes>
</PropertyGroup>
```

### 8.4 Create Migration Guide for Users

**docs/migration/MIGRATION-FROM-NET9.md**:

```markdown
# Migrating Your Koan Framework Project to .NET 10

This guide helps you migrate projects using Koan Framework from .NET 9 to .NET 10.

## Prerequisites
1. Install .NET 10 RC 1 SDK
2. Update Koan Framework packages to 0.7.0-rc.1

## Step-by-Step Migration
1. Update TargetFramework to net10.0
2. Update Koan package references
3. Address breaking changes
4. Test your application

## Breaking Changes
...
```

### 8.5 Test Package Generation

```bash
# Pack all framework projects
dotnet pack src/ --configuration Release --output ./packages

# Verify package contents
dotnet nuget push packages/*.nupkg --source local-test --skip-duplicate

# Test package consumption in sample project
```

**Success Criteria**:
- ✅ All documentation updated
- ✅ CHANGELOG complete
- ✅ Package metadata updated
- ✅ Migration guide created
- ✅ NuGet packages build successfully

---

## Phase 9: Final Validation and Go-Live

**Duration**: 1 day
**Risk**: Low

### 9.1 Final Checklist

```markdown
- [ ] All .csproj files target net10.0
- [ ] All package references updated to .NET 10 versions
- [ ] All breaking changes addressed
- [ ] All Koan patterns tested and working
- [ ] All samples run successfully
- [ ] All Dockerfiles updated
- [ ] All tests passing (100% pass rate)
- [ ] Documentation updated
- [ ] CHANGELOG complete
- [ ] No build warnings (except expected)
- [ ] Performance acceptable
- [ ] Migration guide complete
```

### 9.2 Create Release Branch

```bash
# Merge feature branch to release branch
git checkout -b release/v0.7.0-rc.1
git merge feature/dotnet-10-rc1-migration

# Tag release
git tag -a v0.7.0-rc.1 -m "Koan Framework v0.7.0 RC 1 - .NET 10 Support"

# Push to remote
git push origin release/v0.7.0-rc.1
git push origin v0.7.0-rc.1
```

### 9.3 Create GitHub Release

1. Go to GitHub → Releases → New Release
2. Tag: `v0.7.0-rc.1`
3. Title: "Koan Framework v0.7.0 RC 1 - .NET 10 Support"
4. Description:
```markdown
# Koan Framework v0.7.0 RC 1

First release with .NET 10 RC 1 support!

## ✨ What's New
- Full .NET 10 RC 1 compatibility
- Updated all dependencies
- Updated Docker base images

## ⚠️ Breaking Changes
- Cookie authentication behavior for API endpoints
- Obsolete WebHostBuilder pattern removed
- See [CHANGELOG.md](./CHANGELOG.md) for details

## 📦 Package Versions
- Sylin.Koan.Core: 0.7.0-rc.1
- Sylin.Koan.Web: 0.7.0-rc.1
- Sylin.Koan.Data.*: 0.7.0-rc.1

## 📚 Documentation
- [Migration Guide](./docs/migration/MIGRATION-FROM-NET9.md)
- [Installation Guide](./docs/migration/DOTNET-10-RC1-INSTALLATION.md)

## 🧪 Testing
- 100% unit test pass rate
- All integration tests passing
- Tested across PostgreSQL, MongoDB, SQLite, Redis

## 🐛 Known Issues
- None currently identified

## 📝 Notes
This is a Release Candidate. While it has go-live support from Microsoft,
please test thoroughly before production deployment.
```
5. Attach packages (if publishing manually)

### 9.4 Publish NuGet Packages

```bash
# Publish to NuGet.org (if ready)
dotnet nuget push packages/*.nupkg \
  --source https://api.nuget.org/v3/index.json \
  --api-key $NUGET_API_KEY \
  --skip-duplicate

# Or publish to GitHub Packages
dotnet nuget push packages/*.nupkg \
  --source https://nuget.pkg.github.com/sylin-labs/index.json \
  --api-key $GITHUB_TOKEN \
  --skip-duplicate
```

### 9.5 Announce Release

**Channels**:
- GitHub Discussions
- Twitter/X
- LinkedIn
- Internal team notifications

**Message Template**:
```
🎉 Koan Framework v0.7.0 RC 1 is now available!

✨ First release with .NET 10 RC 1 support
🔧 Updated all dependencies
🐳 .NET 10 Docker images

📚 Full migration guide available
🧪 100% test pass rate

Get started: https://github.com/sylin-labs/Koan-framework/releases/tag/v0.7.0-rc.1

#dotnet #dotnet10 #koanframework
```

---

## Rollback Plan

If critical issues are discovered after migration:

### Immediate Rollback

```bash
# Switch back to main/dev branch
git checkout dev

# Revert local changes
git reset --hard origin/dev

# Rebuild with .NET 9
dotnet restore
dotnet build
```

### Partial Rollback

If only specific components fail:

```bash
# Keep .NET 10 changes but revert problematic package
# Edit .csproj to downgrade specific package
dotnet restore
dotnet build

# Test again
dotnet test
```

### NuGet Package Rollback

```bash
# Unlist packages from NuGet (if published)
# Cannot delete, but can unlist to hide from searches

# Contact users to roll back to previous version
```

---

## Risk Mitigation Summary

| Risk | Probability | Impact | Mitigation |
|------|-------------|--------|------------|
| Provider incompatibility | Medium | High | Test early, have fallback versions |
| Breaking change missed | Low | High | Comprehensive testing, code search |
| Performance regression | Low | Medium | Benchmark testing, comparison |
| Test failures | Medium | Medium | Incremental fixing, isolation |
| Docker build issues | Low | Low | Test early, use specific tags |
| User migration issues | Medium | Low | Detailed guide, support channels |

---

## Success Metrics

### Technical Metrics
- ✅ Build success rate: 100%
- ✅ Test pass rate: 100%
- ✅ Performance regression: < 10%
- ✅ Zero critical bugs
- ✅ All samples functional

### Process Metrics
- ⏱️ Migration duration: 2-3 weeks (target)
- 📝 Documentation coverage: 100%
- 🧪 Test coverage: Maintained or improved
- 🐛 Issue backlog: All resolved before release

---

## Post-Migration Tasks

### Monitor
- GitHub issues for migration-related problems
- NuGet download metrics
- User feedback on breaking changes

### Support
- Respond to migration questions
- Update docs based on feedback
- Create FAQ if common issues emerge

### Iterate
- Apply lessons learned to future migrations
- Improve automation scripts
- Enhance testing coverage

---

## Timeline Summary

| Phase | Days | Dates (Estimated) |
|-------|------|-------------------|
| 1. Foundation | 2-3 | Day 1-3 |
| 2. Dependencies | 3-5 | Day 4-8 |
| 3. Breaking Changes | 3-4 | Day 9-12 |
| 4. Pattern Validation | 4-6 | Day 13-18 |
| 5. Sample Testing | 3-4 | Day 19-22 |
| 6. Container Testing | 2-3 | Day 23-25 |
| 7. Test Suite | 2-3 | Day 26-28 |
| 8. Documentation | 1-2 | Day 29-30 |
| 9. Release | 1 | Day 31 |

**Total**: ~18-31 days (3-4 weeks)

---

## Contact and Support

**Migration Lead**: [TBD]
**Technical Questions**: GitHub Discussions
**Issues**: https://github.com/sylin-labs/Koan-framework/issues
**Slack/Discord**: [TBD]

---

**Document Version**: 1.0
**Last Updated**: 2025-10-01
**Migration Branch**: `feature/dotnet-10-rc1-migration`
**Status**: Ready to Execute
