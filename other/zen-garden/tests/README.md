# Zen Garden Test Suite

## Quick Start

```bash
# Run all tests
./run-all.sh

# Or step by step:
cd tests
docker-compose -f docker-compose.test.yml up -d
sleep 5
cargo test --workspace
docker-compose -f docker-compose.test.yml down
```

## Test Environment

3-Stone Docker Compose setup:
- `stone-01` on 172.20.0.11:3001
- `stone-02` on 172.20.0.12:3001
- `stone-03` on 172.20.0.13:3001

Each Stone runs Garden-Moss daemon with UDP discovery (port 3004).

## Test Scenarios

### Unit Tests
- Shared type serialization (zen-common)
- Discovery request/response round-trip

### Integration Tests (Manual)
1. UDP discovery across 3 Stones
2. Offer service to each Stone
3. List services on all Stones
4. Upgrade all services garden-wide
5. Remove services and verify

## CI Integration

Tests run automatically in GitHub Actions:
- Linux (cargo test)
- Windows (cargo test --bin garden-rake)
- Docker (3-Stone compose)
