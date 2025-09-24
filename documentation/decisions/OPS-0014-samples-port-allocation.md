---
id: OPS-0014
slug: OPS-0014-samples-port-allocation
domain: OPS
status: Accepted
date: 2025-08-17
title: Samples port allocation scheme
---
 
# ADR-0014: Samples port allocation scheme

## Context
Common ports like 5000/8080/27017 often collide with local services. We want predictable, low-conflict ports for all Koan samples, across local runs and compose.

## Decision
Reserve contiguous blocks of 10 ports per sample starting at 5034 (visual hint to “Koan”). The first port in each block is the app’s HTTP binding. Internal container ports mirror the external choice for clarity.

- Range start: 5034
- Block size: 10 per sample
- Assignments (initial):
  - S1: 5044 (block 5040–5049). HTTP binds to 5044.
  - S2: 5054 (block 5050–5059). HTTP binds to 5054. Mongo is addressed by service name in compose (no host port mapping).
  - S5: 5084 (block 5080–5089). HTTP binds to 5084.
  - Future samples: increment the block (S3 → 5060–5069, S4 → 5070–5079, ...).

## Consequences
- Lower chance of conflicts with common dev ports.
- Easy mental model: sample N tends to use 50(40+N)4 as the app port.
- Compose files avoid binding database service ports on the host by default. Connection strings target service names within the compose network.

Note: For developer inspection and demos, samples MAY optionally expose database/vector/aux ports on the host using free ports from within the same block (e.g., S5 → 5081 for Mongo, 5082 for Weaviate, 5083 for Ollama). This is a usability choice and not required in production.

## Implementation notes
- Scripts and Dockerfiles updated to the new ports.
- Compose files map host:container with the reserved ports for APIs; DB services remain internal.
- Documentation updated in sample READMEs and this ADR.
 - When inspection is desired, expose infra ports explicitly in compose using the sample block (e.g., 5081–5083 for S5) and keep data directories host-mounted under the sample’s `data/` folder for easy backup.
