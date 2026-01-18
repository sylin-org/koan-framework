# Zen Garden Offering Compatibility: Sources & Notes

This folder contains `*.compatibility.yaml` manifests used by moss to make **Pass / Fallback / Fail** decisions before install, and to provide targeted suggestions based on post-install log scanning.

These manifests intentionally focus on signals moss can detect today:

- CPU model string and CPU flags (from `/proc/cpuinfo`)
- CPU architecture (Rust `std::env::consts::ARCH`)
- Total memory (host)
- Service log patterns (post-install scan)

## Offering notes

### Milvus
- SIMD requirement: Milvus documents SIMD requirements (SSE4.2/AVX/AVX2/AVX-512) for x86.
- Used as the basis for the `x86_64 + missing sse4_2` deny rule.
- Reference: https://milvus.io/docs/requirements.md

### SQL Server (containers)
- SQL Server Linux container images are supported on **x86_64/amd64**; installs on ARM typically fail with “no matching manifest”.
- Basis for the `architectures: [aarch64, arm64, armv7l, armv6l]` deny rule.
- Reference: https://learn.microsoft.com/en-us/sql/linux/quickstart-install-connect-docker

### OpenSearch / Elasticsearch
- Both are JVM + Lucene-based and commonly require host kernel tuning (notably `vm.max_map_count`).
- We cannot pre-check sysctls in compatibility rules yet, so we provide **post-install log pattern guidance** instead.

## Assumptions (conservative)

- Memory thresholds are *practical minimums* aimed at preventing obviously-broken installs on low-RAM stones.
- Architecture-deny rules are only used where the upstream image ecosystem is known to be architecture-limited (SQL Server).
