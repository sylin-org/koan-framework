# Async Job System - API Guide

## Overview

Moss now supports **non-blocking async deployments** with job tracking. Service installations happen in the background, allowing you to queue multiple deployments without waiting.

---

## API Changes

### Old Behavior (Synchronous)
```bash
POST /api/operations/offer/mongodb
# Blocks for 2-5 seconds until Docker pull/create completes
# Returns: {"status": "installed", "offering": "mongodb"}
```

### New Behavior (Asynchronous)
```bash
POST /api/operations/offer/mongodb
# Returns immediately (202 Accepted)
# Response: {"status": "accepted", "job_id": "025d5981-...", "offering": "mongodb"}
```

---

## Endpoints

### 1. Deploy Single Service (Async)
**POST** `/api/operations/offer/:offering`

```bash
curl -X POST http://stone-01:3001/api/operations/offer/mongodb
```

**Response (202 Accepted):**
```json
{
  "status": "accepted",
  "job_id": "025d5981-be96-43d5-a655-d6d4f337b897",
  "offering": "mongodb",
  "message": "Installation started, check /api/jobs/{job_id} for status"
}
```

### 2. Deploy Multiple Services (Batch)
**POST** `/api/operations/offer`

**Option A: Comma-separated string**
```bash
curl -X POST http://stone-01:3001/api/operations/offer \
  -H "Content-Type: application/json" \
  -d '{"offerings": "mongodb,redis,postgresql"}'
```

**Option B: Array**
```bash
curl -X POST http://stone-01:3001/api/operations/offer \
  -H "Content-Type: application/json" \
  -d '{"offerings": ["mongodb", "redis", "postgresql"]}'
```

**Response (202 Accepted):**
```json
{
  "status": "accepted",
  "job_id": "4ee14125-13bf-471a-b0f2-b3cc41b547b8",
  "offerings": ["mongodb", "redis", "postgresql"],
  "message": "Batch installation started, check /api/jobs/{job_id} for status"
}
```

### 3. Check Job Status
**GET** `/api/jobs/:job_id`

```bash
curl http://stone-01:3001/api/jobs/025d5981-be96-43d5-a655-d6d4f337b897
```

**Response:**
```json
{
  "id": "025d5981-be96-43d5-a655-d6d4f337b897",
  "offerings": ["mongodb"],
  "status": "Completed",
  "completed": ["mongodb"],
  "failed": {},
  "started_at": {"secs_since_epoch": 1768538600, "nanos_since_epoch": 998682093},
  "completed_at": {"secs_since_epoch": 1768538602, "nanos_since_epoch": 188294283}
}
```

**Job Statuses:**
- `Pending` - Job created, not started yet
- `Running` - Currently installing services
- `Completed` - All services installed successfully
- `Failed` - One or more services failed (check `failed` field)

### 4. List All Jobs
**GET** `/api/jobs`

```bash
curl http://stone-01:3001/api/jobs
```

**Response:**
```json
[
  {
    "id": "4ee14125-13bf-471a-b0f2-b3cc41b547b8",
    "offerings": ["redis", "postgresql", "vault"],
    "status": "Completed",
    "completed": ["redis", "postgresql", "vault"],
    "failed": {},
    "started_at": {...},
    "completed_at": {...}
  },
  {
    "id": "025d5981-be96-43d5-a655-d6d4f337b897",
    "offerings": ["mongodb"],
    "status": "Completed",
    "completed": ["mongodb"],
    "failed": {},
    "started_at": {...},
    "completed_at": {...}
  }
]
```

---

## Use Cases

### 1. Deploy Multiple Services at Once
Instead of sequential blocking requests:
```bash
# OLD WAY (slow, blocks for each service)
curl -X POST http://stone-01:3001/api/operations/offer/mongodb  # waits 3s
curl -X POST http://stone-01:3001/api/operations/offer/redis    # waits 2s
curl -X POST http://stone-01:3001/api/operations/offer/vault    # waits 2s
# Total: ~7 seconds
```

Use batch deployment:
```bash
# NEW WAY (fast, non-blocking)
curl -X POST http://stone-01:3001/api/operations/offer \
  -H "Content-Type: application/json" \
  -d '{"offerings": "mongodb,redis,vault"}'
# Returns immediately, services install in background
```

### 2. Poll Job Status Until Complete
```bash
JOB_ID=$(curl -s -X POST http://stone-01:3001/api/operations/offer/mongodb | jq -r '.job_id')

while true; do
  STATUS=$(curl -s http://stone-01:3001/api/jobs/$JOB_ID | jq -r '.status')
  echo "Job status: $STATUS"
  
  if [ "$STATUS" == "Completed" ] || [ "$STATUS" == "Failed" ]; then
    break
  fi
  
  sleep 1
done

curl http://stone-01:3001/api/jobs/$JOB_ID | jq
```

### 3. Deploy Entire Stack
```bash
# Deploy full data layer
curl -X POST http://stone-01:3001/api/operations/offer \
  -H "Content-Type: application/json" \
  -d '{"offerings": "mongodb,postgresql,redis,vault,rabbitmq"}'
```

---

## PowerShell Examples

### Single Service
```powershell
$response = Invoke-RestMethod -Uri "http://stone-01:3001/api/operations/offer/mongodb" -Method POST
$jobId = $response.job_id

# Wait for completion
do {
    Start-Sleep -Seconds 1
    $job = Invoke-RestMethod -Uri "http://stone-01:3001/api/jobs/$jobId"
    Write-Host "Status: $($job.status)"
} while ($job.status -eq "Running" -or $job.status -eq "Pending")

$job | ConvertTo-Json
```

### Batch Deployment
```powershell
$offerings = @("mongodb", "redis", "postgresql", "vault")

$response = Invoke-RestMethod -Uri "http://stone-01:3001/api/operations/offer" `
    -Method POST `
    -ContentType "application/json" `
    -Body (@{offerings = $offerings} | ConvertTo-Json)

Write-Host "Job ID: $($response.job_id)"
Write-Host "Deploying: $($response.offerings -join ', ')"

# Check final status
Start-Sleep -Seconds 10
$job = Invoke-RestMethod -Uri "http://stone-01:3001/api/jobs/$($response.job_id)"
Write-Host "Completed: $($job.completed -join ', ')"
if ($job.failed.Count -gt 0) {
    Write-Host "Failed: $($job.failed | ConvertTo-Json)"
}
```

---

## Error Handling

### Partial Failures in Batch Jobs
If some services fail, the job status will be `Failed`, but `completed` will list successful services:

```json
{
  "id": "...",
  "offerings": ["mongodb", "invalid-service", "redis"],
  "status": "Failed",
  "completed": ["mongodb", "redis"],
  "failed": {
    "invalid-service": "Template not found: No such offering"
  },
  "started_at": {...},
  "completed_at": {...}
}
```

### Job Not Found
```bash
curl http://stone-01:3001/api/jobs/invalid-job-id
```

**Response (404 Not Found):**
```json
{
  "error": "job_not_found",
  "message": "Job invalid-job-id not found"
}
```

---

## Performance Comparison

| Scenario | Old (Sync) | New (Async) | Improvement |
|----------|-----------|-------------|-------------|
| Single service | 3s block | <100ms response | 30x faster |
| 5 services (sequential) | ~15s total | <100ms response | 150x faster |
| 5 services (batch) | N/A | ~10s background | Non-blocking |

---

## Migration Guide

### If you have scripts using the old API:

**Before:**
```bash
curl -X POST http://stone:3001/api/operations/offer/mongodb
# Response: {"status": "installed", "offering": "mongodb"}
```

**After:**
```bash
# Option 1: Fire and forget
curl -X POST http://stone:3001/api/operations/offer/mongodb

# Option 2: Wait for completion
JOB_ID=$(curl -s -X POST http://stone:3001/api/operations/offer/mongodb | jq -r '.job_id')
while [ "$(curl -s http://stone:3001/api/jobs/$JOB_ID | jq -r '.status')" != "Completed" ]; do
    sleep 1
done
```

### Breaking Changes
- Response status changed from `201 Created` to `202 Accepted`
- Response body now includes `job_id` instead of immediate confirmation
- To get service status, poll `/api/jobs/:job_id` or check `/api/services`
