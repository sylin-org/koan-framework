# Entity ID Storage Optimization - Migration Strategy

## Migration Overview

The Entity ID Storage Optimization system is designed for **zero-downtime deployment** with **automatic Entity<> pattern detection**. The system automatically optimizes `Entity<Model>` entities (implicit string keys) while respecting explicit developer choices in `Entity<Model, string>` patterns. Optimization metadata is cached using AggregateBag integration for optimal performance.

## Migration Principles

1. **No Breaking Changes**: All existing APIs remain unchanged
2. **Automatic Optimization**: `Entity<Model>` patterns optimized by default
3. **Respect Developer Intent**: `Entity<Model, string>` patterns preserve explicit string choice
4. **Gradual Storage Evolution**: Database schema can evolve over time to native types
5. **Data Integrity**: No data loss during optimization process
6. **Zero Configuration**: No developer action required for standard cases

## Deployment Strategy

### Phase 1: Infrastructure Deployment (Week 1)

#### Code Deployment

```bash
# Deploy optimization infrastructure (inactive by default)
git deploy feature/entity-id-optimization-phase1

# Verify deployment
kubectl get pods -l app=koan-api
kubectl logs -l app=koan-api --tail=50 | grep "StorageOptimization"
```

#### Configuration Setup

```json
{
  "Koan": {
    "Data": {
      "Optimization": {
        "Enabled": false, // Start disabled
        "LogLevel": "Debug", // Verbose logging initially
        "SafeMode": true // Extra validation
      }
    }
  }
}
```

#### Validation Steps

1. **Application Starts**: Automatic optimization active for Entity<> patterns
2. **Bootstrap Logging**: AggregateBag analysis identifies optimization candidates
3. **Performance Baseline**: Monitor Entity<> entities for optimization application

### Phase 2: Automatic Optimization Active (Week 2-3)

#### Automatic Pattern Detection

**No Configuration Required** - The system automatically:

```csharp
// AUTOMATICALLY OPTIMIZED (Entity<Model> pattern)
public class MediaFormat : Entity<MediaFormat>
{
    // GUID optimization applied automatically
}

// NOT OPTIMIZED (explicit string choice respected)
public class UserProfile : Entity<UserProfile, string>
{
    // String storage preserved as developer intended
}
```

#### Override Configuration (Optional)

```csharp
// Force optimization ON
[OptimizeStorage(OptimizationType = StorageOptimizationType.Guid)]
public class SpecialEntity : Entity<SpecialEntity, string>

// Force optimization OFF
[OptimizeStorage(OptimizationType = StorageOptimizationType.None)]
public class UnoptimizedEntity : Entity<UnoptimizedEntity>
```

#### Monitoring Setup

```yaml
# Prometheus metrics for monitoring
- koan_entity_optimization_enabled{entity_type}
- koan_id_conversion_duration_ms{operation,entity_type}
- koan_storage_bytes_saved{entity_type}
- koan_query_performance_improvement{entity_type}
```

#### Validation Checklist

- [ ] Entity<> pattern entities show automatic optimization
- [ ] Entity<,string> pattern entities remain unoptimized
- [ ] No errors in inheritance pattern detection
- [ ] API responses remain identical
- [ ] Query performance improves for optimized entities

### Phase 3: Production Rollout (Week 4-6)

#### Automatic Optimization Coverage

**Automatic**: All `Entity<Model>` patterns optimized by default across the application
**Expected**: Most entities using Entity<> pattern receive GUID optimization automatically

#### Database Migration (Background)

```sql
-- PostgreSQL: Gradual migration to uuid columns
-- Run during low-traffic periods

-- 1. Add new optimized column
ALTER TABLE products ADD COLUMN id_uuid UUID;

-- 2. Populate new column (batched)
UPDATE products
SET id_uuid = id::uuid
WHERE id_uuid IS NULL
  AND id ~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$'
LIMIT 1000;

-- 3. Create indexes on new column
CREATE INDEX CONCURRENTLY products_id_uuid_idx ON products(id_uuid);

-- 4. Switch primary key (maintenance window)
ALTER TABLE products DROP CONSTRAINT products_pkey;
ALTER TABLE products ADD PRIMARY KEY (id_uuid);
ALTER TABLE products DROP COLUMN id;
ALTER TABLE products RENAME COLUMN id_uuid TO id;
```

### Phase 4: Full Production Use (Week 7-8)

#### Automatic Optimization Active

**No Configuration Required** - The system automatically optimizes based on inheritance patterns:

```csharp
// Summary of automatic behavior:
Entity<Model>         → GUID optimization applied
Entity<Model, string> → No optimization (respects explicit choice)
IEntity<string>       → No optimization (explicit implementation)

// Optional overrides available via attributes when needed
[OptimizeStorage(OptimizationType = StorageOptimizationType.None)]
public class CustomEntity : Entity<CustomEntity> // Disables default optimization
```

## Data Migration Procedures

### Zero-Downtime Column Migration

#### PostgreSQL Migration Script

```sql
-- Migration script: string_id_to_uuid.sql
BEGIN;

-- 1. Safety check: Verify all IDs are valid UUIDs
DO $$
DECLARE
    invalid_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO invalid_count
    FROM {table_name}
    WHERE id !~ '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$';

    IF invalid_count > 0 THEN
        RAISE EXCEPTION 'Found % invalid UUID strings. Migration aborted.', invalid_count;
    END IF;
END $$;

-- 2. Add new UUID column
ALTER TABLE {table_name} ADD COLUMN id_new UUID;

-- 3. Populate new column
UPDATE {table_name} SET id_new = id::uuid;

-- 4. Add NOT NULL constraint
ALTER TABLE {table_name} ALTER COLUMN id_new SET NOT NULL;

-- 5. Create new index
CREATE INDEX CONCURRENTLY {table_name}_id_new_idx ON {table_name}(id_new);

-- 6. Drop old constraint and rename (requires brief lock)
ALTER TABLE {table_name} DROP CONSTRAINT {table_name}_pkey;
ALTER TABLE {table_name} ADD PRIMARY KEY (id_new);
ALTER TABLE {table_name} DROP COLUMN id;
ALTER TABLE {table_name} RENAME COLUMN id_new TO id;

COMMIT;
```

### Rollback Procedures

#### Emergency Rollback

```bash
# 1. Disable optimization immediately
kubectl patch configmap koan-config --patch='{"data":{"optimization.enabled":"false"}}'

# 2. Restart applications
kubectl rollout restart deployment/koan-api

# 3. Verify rollback success
kubectl logs -l app=koan-api | grep "Optimization disabled"
```

#### Data Rollback (if needed)

```sql
-- Rollback to string storage (PostgreSQL)
BEGIN;

-- 1. Add string column back
ALTER TABLE {table_name} ADD COLUMN id_string TEXT;

-- 2. Populate string column
UPDATE {table_name} SET id_string = id::text;

-- 3. Switch back to string primary key
ALTER TABLE {table_name} DROP CONSTRAINT {table_name}_pkey;
ALTER TABLE {table_name} ADD PRIMARY KEY (id_string);
ALTER TABLE {table_name} DROP COLUMN id;
ALTER TABLE {table_name} RENAME COLUMN id_string TO id;

COMMIT;
```

## Testing Strategy

### Pre-Migration Testing

#### 1. Load Testing

```yaml
# load-test-config.yaml
scenarios:
  - name: "baseline_performance"
    optimization_enabled: false
    duration: "5m"
    users: 100

  - name: "optimized_performance"
    optimization_enabled: true
    duration: "5m"
    users: 100

  - name: "mixed_entities"
    optimization_enabled: true
    optimized_entities: ["TestEntity"]
    duration: "10m"
    users: 200
```

#### 2. Data Integrity Testing

```csharp
[Test]
public async Task VerifyDataIntegrity_BeforeAndAfterOptimization()
{
    // 1. Create test entities with optimization disabled
    var entities = await CreateTestEntities(1000);

    // 2. Enable optimization
    await EnableOptimizationForEntity<TestEntity>();

    // 3. Verify all entities remain accessible
    foreach (var entity in entities)
    {
        var retrieved = await Data<TestEntity, string>.GetAsync(entity.Id);
        Assert.AreEqual(entity, retrieved);
    }

    // 4. Verify CRUD operations work correctly
    await PerformCrudOperations<TestEntity>();
}
```

### Migration Validation

#### Performance Validation Script

```csharp
public class MigrationValidator
{
    public async Task<ValidationReport> ValidateMigration<TEntity>()
        where TEntity : class, IEntity<string>, new()
    {
        var report = new ValidationReport();

        // 1. Storage efficiency check
        var storageStats = await GetStorageStats<TEntity>();
        report.StorageSavings = storageStats.CalculateSavings();

        // 2. Query performance check
        var queryStats = await BenchmarkQueryPerformance<TEntity>();
        report.QueryImprovement = queryStats.CalculateImprovement();

        // 3. Data integrity check
        var integrityCheck = await ValidateDataIntegrity<TEntity>();
        report.DataIntegrityScore = integrityCheck.Score;

        return report;
    }
}
```

## Monitoring and Alerting

### Key Metrics to Monitor

#### 1. Optimization Health

```yaml
alerts:
  - alert: EntityOptimizationFailed
    expr: increase(koan_entity_optimization_errors_total[5m]) > 0
    severity: warning

  - alert: IDConversionErrors
    expr: increase(koan_id_conversion_errors_total[5m]) > 5
    severity: critical

  - alert: PerformanceRegression
    expr: koan_query_duration_ms > (koan_query_baseline_ms * 1.2)
    severity: warning
```

#### 2. Storage Efficiency

```yaml
dashboards:
  - name: "Entity Storage Optimization"
    panels:
      - title: "Storage Bytes Saved"
        metric: sum(koan_storage_bytes_saved) by (entity_type)

      - title: "Query Performance Improvement"
        metric: koan_query_performance_improvement by (entity_type)

      - title: "Optimization Coverage"
        metric: count(koan_entity_optimization_enabled == 1)
```

### Health Checks

#### Application Health Endpoint

```csharp
[HttpGet("health/optimization")]
public IActionResult OptimizationHealth()
{
    var health = new
    {
        OptimizationEnabled = _options.Enabled,
        OptimizedEntityCount = EntityStorageCache.OptimizedEntityTypes.Count,
        ConversionErrors = _metrics.ConversionErrors,
        StorageSavings = _metrics.TotalStorageSavings,
        Status = _metrics.ConversionErrors == 0 ? "Healthy" : "Degraded"
    };

    return Ok(health);
}
```

## Risk Mitigation

### Risk Assessment Matrix

| Risk                   | Probability | Impact   | Mitigation                                    |
| ---------------------- | ----------- | -------- | --------------------------------------------- |
| ID Conversion Errors   | Low         | High     | Extensive testing, graceful fallback          |
| Performance Regression | Low         | Medium   | Load testing, monitoring alerts               |
| Data Corruption        | Very Low    | Critical | Comprehensive validation, rollback procedures |

### Contingency Plans

#### 1. Performance Issues

```yaml
response:
  - immediate: Disable optimization for affected entity types
  - short_term: Investigate and fix performance bottlenecks
  - long_term: Optimize conversion algorithms
```

#### 2. Data Integrity Issues

```yaml
response:
  - immediate: Stop new optimizations, enable safe mode
  - short_term: Validate data integrity, fix corruption if any
  - long_term: Enhance validation procedures
```

## Success Criteria

### Migration Success Metrics

- [ ] Zero data loss during migration
- [ ] No API breaking changes
- [ ] 50%+ storage reduction for optimized entities
- [ ] 2-5x query performance improvement
- [ ] <0.1% error rate during conversion operations

### Operational Success Metrics

- [ ] Application stability maintained
- [ ] Response time SLA compliance
- [ ] Zero unplanned downtime
- [ ] Successful rollback capability demonstrated
- [ ] Monitoring and alerting functioning correctly

This migration strategy ensures a safe, gradual transition to optimized ID storage while maintaining system reliability and data integrity throughout the process.
