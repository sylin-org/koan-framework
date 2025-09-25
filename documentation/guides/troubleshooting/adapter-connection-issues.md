# Adapter Connection Issues - Troubleshooting Guide

**Document Type**: TROUBLESHOOTING
**Target Audience**: Developers, DevOps Engineers
**Last Updated**: 2025-01-27
**Framework Version**: v0.2.18+

---

## Problem: Adapter Connection Failures

### Common Symptoms
- `SocketNotAvailableException: MultiplexingConnection`
- `Service n1ql is either not configured or cannot be reached`
- `Schema auto-provisioning failed for [Entity]`
- `Operation cancelled (External) after [time]ms`
- Empty collections/endpoints returning `[]` despite expecting data

---

## 🚨 Quick Diagnosis Checklist

### 1. Check Container Status
```bash
# Verify all containers are running
docker ps

# Check specific adapter container logs
docker logs [container-name] --tail 50

# Look for these key patterns:
# ✅ "Cluster initialized successfully"
# ✅ "bootstrap completed"
# ❌ "Connection refused"
# ❌ "Service [service] not configured"
```

### 2. Verify Adapter Readiness
```bash
# Check application startup logs for adapter status
docker logs [api-container] | grep -E "(Healthy|StartupProbe|data:)"

# Look for:
# ✅ "StartupProbe: data:[provider] -> Healthy"
# ❌ Missing readiness messages
# ❌ Exception traces during startup
```

### 3. Test Direct Connectivity
```bash
# For Couchbase example:
curl -s http://localhost:[port]/pools/default

# For other providers, check their health endpoints
# Expected: JSON response or HTTP 200, not connection refused
```

---

## 🔍 Root Cause Analysis

### Architecture Understanding
The Koan Framework uses a **multi-layer provisioning system**:

1. **Infrastructure Layer**: Database cluster/service initialization
2. **Framework Layer**: SDK connectivity and readiness
3. **Application Layer**: Schema and collection provisioning

**Connection failures occur when these layers aren't properly coordinated.**

### Common Failure Points

#### 1. **Infrastructure Not Ready**
- **Symptom**: `Connection refused` errors
- **Cause**: Database container starting but service not listening
- **Location**: Infrastructure layer

#### 2. **SDK Bootstrap Incomplete**
- **Symptom**: `SocketNotAvailableException`, operation timeouts
- **Cause**: Framework connected but SDK not fully initialized
- **Location**: Framework layer

#### 3. **Service Dependencies Missing**
- **Symptom**: `Service [name] not configured or cannot be reached`
- **Cause**: Required database services (like N1QL) not enabled
- **Location**: Infrastructure + Framework layers

#### 4. **Provisioning Timing Issues**
- **Symptom**: `Schema auto-provisioning failed`
- **Cause**: Collections created but not immediately query-ready
- **Location**: Framework + Application layers

---

## 🛠️ Resolution Steps

### Step 1: Infrastructure Readiness

#### For Couchbase Issues:
```bash
# 1. Check if cluster is initialized
curl -u "username:password" http://localhost:[port]/pools/default

# 2. Verify required services are enabled
curl -u "username:password" http://localhost:[port]/pools/default/buckets

# 3. Test N1QL service specifically
curl -u "username:password" -X POST http://localhost:[port]/query/service \
  -d "statement=SELECT 1 AS test"
```

#### For Other Providers:
- **MongoDB**: Test `db.runCommand({ping: 1})`
- **PostgreSQL**: Test `SELECT 1` query
- **Vector DB**: Check health endpoint

**If infrastructure fails**: Restart containers with clean volumes:
```bash
cd [project-directory]
docker compose down -v
rm -rf data/  # Remove persistent data
docker compose up -d
```

### Step 2: Framework Layer Fixes

#### Common Fixes Applied to CouchbaseClusterProvider:

**Add SDK Bootstrap Waiting:**
```csharp
// In cluster connection code
_cluster = await Cluster.ConnectAsync(connectionString, options);

// ADD: Wait for SDK to be fully ready
await _cluster.WaitUntilReadyAsync(TimeSpan.FromSeconds(30));
```

**Add Bucket Readiness:**
```csharp
// After getting bucket reference
_bucket = await cluster.BucketAsync(bucketName);

// ADD: Wait for bucket to be ready
await _bucket.WaitUntilReadyAsync(TimeSpan.FromSeconds(10));
```

**Add Service-Specific Readiness:**
```csharp
// For Couchbase N1QL specifically
private async Task WaitForN1QLServiceReadinessAsync(string baseUrl, string username, string password, CancellationToken ct)
{
    for (int i = 0; i < maxRetries; i++)
    {
        try
        {
            // Test actual N1QL query
            var response = await httpClient.PostAsync($"{baseUrl}/query/service",
                new FormUrlEncodedContent(new[] {
                    new KeyValuePair<string, string>("statement", "SELECT 1 AS test")
                }), ct);

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(ct);
                if (content.Contains("\"test\":1"))
                {
                    _logger?.LogInformation("N1QL query service is ready");
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("Waiting for N1QL readiness (attempt {Attempt})", i + 1);
        }

        await Task.Delay(3000, ct);
    }
}
```

### Step 3: Application Layer Fixes

#### Collection Auto-Provisioning Timing:
```csharp
// In EnsureCollectionAsync methods
await manager.CreateCollectionAsync(spec);
_logger?.LogInformation("Created collection: {Collection}", collectionName);

// ADD: Wait for collection to be query-ready
await Task.Delay(2000, ct);
_logger?.LogDebug("Collection {Collection} ready for queries", collectionName);
```

### Step 4: Restart and Verify

```bash
# Rebuild with fixes
docker compose build --no-cache [api-service]

# Restart services
docker compose down && docker compose up -d

# Monitor logs for successful initialization
docker logs [api-container] | grep -E "(initialized|ready|Healthy)"

# Test endpoints
curl http://localhost:[port]/api/[endpoint]
```

---

## 🔧 Advanced Debugging

### Enable Debug Logging
```json
// In appsettings.Development.json
{
  "Logging": {
    "LogLevel": {
      "Koan.Data.*": "Debug",
      "Koan.Core.Adapters": "Debug"
    }
  }
}
```

### Monitor Specific Components
```bash
# Watch adapter readiness
docker logs [container] --follow | grep -E "(Readiness|Healthy|StartupProbe)"

# Monitor provisioning
docker logs [container] --follow | grep -E "(provisioning|EnsureCreated|Collection)"

# Track timing issues
docker logs [container] --follow | grep -E "(bootstrap|ready|completed)"
```

### Container Environment Issues
```bash
# Check network connectivity between containers
docker exec [api-container] ping [db-container]

# Verify environment variables
docker exec [api-container] env | grep -i koan

# Test direct database access
docker exec [db-container] [db-specific-command]
```

---

## 🚀 Prevention

### 1. **Proper Health Checks**
```yaml
# In docker-compose.yml
services:
  database:
    healthcheck:
      test: ["CMD", "[health-check-command]"]
      interval: 5s
      timeout: 3s
      retries: 30
      start_period: 30s

  api:
    depends_on:
      database:
        condition: service_healthy
```

### 2. **Startup Readiness**
```csharp
// In adapter implementations
public async Task<bool> IsReadyAsync(CancellationToken ct)
{
    // Test actual operations, not just connectivity
    try
    {
        // Perform a lightweight query/operation
        await TestBasicOperationAsync(ct);
        return true;
    }
    catch
    {
        return false;
    }
}
```

### 3. **Monitoring Integration**
```csharp
// Add specific metrics
_metrics.RecordAdapterConnectionTime(elapsed);
_metrics.IncrementAdapterReadinessFailures(providerName);

// Health endpoint integration
services.AddHealthChecks()
    .AddKoanAdapterHealth();
```

---

## 📞 When to Escalate

**Escalate to Framework Team if:**
- Issues persist after infrastructure + framework fixes
- Multiple providers showing same connection patterns
- Performance degradation in production environments
- Suspected framework-level timing or coordination bugs

**Include in escalation:**
- Complete logs from startup to failure
- Container compose file and configuration
- Steps taken and their results
- Environment details (Docker version, host OS, etc.)

---

*This guide is based on real troubleshooting sessions and should resolve 90%+ of adapter connection issues. For additional patterns, see the [Auto-Provisioning System](../deep-dive/auto-provisioning-system.md) documentation.*