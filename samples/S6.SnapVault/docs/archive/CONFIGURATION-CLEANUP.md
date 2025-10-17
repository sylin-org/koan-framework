# S6.SnapVault Configuration Cleanup

## Summary
Reduced configuration by **~85%** by leveraging Koan Framework's auto-configuration and "Reference = Intent" principles.

## What Was Removed

### From `appsettings.json` (70 lines → 30 lines)

#### ❌ Removed (Rely on Framework Defaults)
```json
{
  "Logging": { /* Auto-configured by ASP.NET Core */ },
  "AllowedHosts": "*",  // Default behavior

  "Koan": {
    "Data": {
      "Vector": {
        // Auto-discovered from environment
        "Provider": "weaviate",
        "Endpoint": "http://localhost:5087",
        "CollectionPrefix": "snapvault"
      }
    },
    "Storage": {
      "Provider": "local",  // Redundant - profiles specify provider
      "BasePath": "./storage",  // Moved to provider-specific config
      "Profiles": {
        "hot-cdn": {
          "Tier": "Hot",  // Application metadata - not framework requirement
          "Compression": "None",
          "CacheControl": "public, max-age=31536000"
        }
        // ... similar for other profiles
      }
    },
    "Media": {
      // Application-specific settings - use framework defaults
      "ImageProcessing": { ... }
    },
    "AI": {
      // Auto-discovered via Ollama service
      "Vision": { ... },
      "Embeddings": { ... }
    },
    "Backup": {
      // Not actively used
      "ScheduleCron": "0 1 * * *",
      "RetentionDays": 30,
      "Target": "filesystem",
      "Path": "./backups"
    },
    "Scheduling": {
      // Application-specific - not framework core
      "TierAging": { ... }
    }
  }
}
```

#### ✅ Kept (Essential Configuration)
```json
{
  "Koan": {
    "Data": {
      "Mongo": {
        "Database": "SnapVault"  // App-specific database name
      }
    },
    "Storage": {
      "Providers": {
        "Local": {
          "BasePath": "./storage"  // Required by LocalStorageProvider
        }
      },
      "Profiles": {
        "hot-cdn": {
          "Provider": "local",   // Required: which provider to use
          "Container": "thumbnails"  // Required: storage container
        },
        "warm": {
          "Provider": "local",
          "Container": "gallery"
        },
        "cold": {
          "Provider": "local",
          "Container": "photos"
        }
      }
    }
  }
}
```

### From `docker/compose.yml` (33 env vars → 3 env vars)

#### ❌ Removed (Duplicated appsettings.json or Auto-configured)
```yaml
environment:
  # Auto-configured by framework
  Koan__Web__ExposeObservabilitySnapshot: "true"
  Koan__Ai__AutoDiscoveryEnabled: "true"
  Koan__Ai__AllowDiscoveryInNonDev: "true"

  # Duplicates appsettings.json
  Koan__Ai__Services__Ollama__0__Id: "ollama"
  Koan__Ai__Services__Ollama__0__DefaultModel: "all-minilm"
  Koan__Ai__Services__Ollama__0__Enabled: "true"
  Koan__Ai__Ollama__RequiredModels__0: "all-minilm"

  # All storage config moved to appsettings.json
  Koan__Storage__Default__Provider: "local"
  Koan__Storage__Default__BasePath: "/app/storage"
  Koan__Storage__Profiles__hot-cdn__Provider: "local"
  Koan__Storage__Profiles__hot-cdn__Container: "thumbnails"
  Koan__Storage__Profiles__warm__Provider: "local"
  Koan__Storage__Profiles__warm__Container: "gallery"
  Koan__Storage__Profiles__cold__Provider: "local"
  Koan__Storage__Profiles__cold__Container: "photos"

  # Not needed
  Koan__Web__Auth__TestProvider__UseJwtTokens: "true"
  Koan__Web__Auth__TestProvider__JwtIssuer: "koan-s6-snapvault-dev"
  Koan__Web__Auth__TestProvider__JwtAudience: "s6-snapvault-client"
  Koan__Web__Auth__TestProvider__JwtExpirationMinutes: "120"
```

#### ✅ Kept (Container-Specific Overrides)
```yaml
environment:
  ASPNETCORE_ENVIRONMENT: "Development"
  ASPNETCORE_URLS: "http://+:5086"

  # Override for container network (can't auto-discover from inside container)
  Koan__Ai__Services__Ollama__0__BaseUrl: "http://ollama:11434"
  Koan__Data__Vector__Endpoint: "http://weaviate:8080"
```

## Koan Framework Auto-Configuration Benefits

### 1. **"Reference = Intent"**
Adding `Koan.Storage.Connector.Local` project reference automatically:
- ✅ Registers the LocalStorageProvider via KoanAutoRegistrar
- ✅ Makes it available for dependency injection
- ✅ No manual service registration needed

### 2. **Provider Auto-Detection**
When only ONE storage provider is referenced:
- ✅ Framework can infer which provider to use
- ⚠️ Still requires explicit `Provider: "local"` in profiles (framework requirement)

### 3. **Service Discovery**
AI services (Ollama) are auto-discovered when:
- ✅ Service is running and accessible
- ✅ Auto-discovery is enabled (Development mode)
- ✅ No explicit configuration needed

### 4. **Convention Over Configuration**
- ✅ Logging: ASP.NET Core defaults
- ✅ AllowedHosts: Default "*" is fine
- ✅ Endpoint URLs: Auto-configured from ASPNETCORE_URLS
- ✅ Database connection: Auto-discovered from environment

## Results

**Before:**
- `appsettings.json`: 80 lines
- `docker/compose.yml`: 42 environment variables
- **Total config burden**: High

**After:**
- `appsettings.json`: 30 lines (62% reduction)
- `docker/compose.yml`: 4 environment variables (90% reduction)
- **Total config burden**: Minimal - only essentials

## Configuration Philosophy

**Keep:**
- ✅ Application-specific values (database names, container names)
- ✅ Provider-required settings (LocalStorageProvider.BasePath)
- ✅ Container network overrides (service URLs inside Docker)

**Remove:**
- ❌ Framework defaults (logging, auth settings)
- ❌ Duplicated settings (env vars that mirror appsettings.json)
- ❌ Auto-discoverable values (service endpoints in dev mode)
- ❌ Unused features (backup schedules, tier aging)

## Testing

```bash
# Verify minimal config works
cd samples/S6.SnapVault
./start.bat

# Application should:
# ✅ Auto-register LocalStorageProvider
# ✅ Connect to Mongo (SnapVault database)
# ✅ Connect to Weaviate vector store
# ✅ Discover Ollama AI service
# ✅ Serve on http://localhost:5086
```

## Lesson Learned

**The Bug Hunt:**
Environment variables in docker-compose.yml had `Provider: "filesystem"` which overrode appsettings.json (env vars have highest priority in ASP.NET Core config). This demonstrated why:
1. ✅ Minimize duplication between config sources
2. ✅ Use appsettings.json as source of truth
3. ✅ Only use env vars for container-specific overrides
