# Ollama Research

## Overview

| Property | Value |
|----------|-------|
| **Official Name** | Ollama |
| **Category** | Local LLM Runtime |
| **Primary Use** | Running large language models locally |
| **License** | MIT |
| **Project URL** | https://ollama.com/ |
| **Docker Hub** | https://hub.docker.com/r/ollama/ollama |
| **GitHub** | https://github.com/ollama/ollama |
| **Runtime** | Go + llama.cpp |

## What is Ollama?

Ollama is a tool for running large language models (LLMs) locally. It:
- Wraps llama.cpp for model inference
- Provides a simple REST API
- Handles model downloading and management
- Supports GPU acceleration (NVIDIA CUDA, AMD ROCm, Apple Metal)
- Can run in CPU-only mode

## Docker Image Analysis

### Image Selection
**Selected**: `ollama/ollama:0.5`

Using pinned version for stability.

### Image Variants

| Image | Use Case |
|-------|----------|
| `ollama/ollama` | CPU or auto-detect GPU |
| `ollama/ollama:rocm` | AMD GPU (ROCm) |

NVIDIA GPU support is automatic if nvidia-container-toolkit is installed.

### Architecture Support

| Architecture | Supported | Notes |
|--------------|-----------|-------|
| amd64 | ✅ | Primary platform |
| arm64 | ✅ | Apple Silicon, Raspberry Pi 4+ |
| arm32 | ❌ | Not supported |

## CPU Requirements

### AVX Instructions

Ollama/llama.cpp **benefits significantly from AVX/AVX2/AVX-512** on x86:
- AVX provides SIMD acceleration for matrix operations
- AVX2 is ~2x faster than AVX
- AVX-512 (where available) provides additional speedup

**Without AVX**: Ollama will still run but inference will be **much slower**.

### CPU Compatibility

| CPU | AVX | Performance |
|-----|-----|-------------|
| Intel Core i3/i5/i7 (Haswell+) | AVX2 | Good |
| Intel Core (Sandy Bridge+) | AVX | Acceptable |
| Intel Celeron J4105 | SSE4.2 only | Very slow |
| Intel Atom | Varies | Very slow |
| AMD Ryzen | AVX2 | Good |
| Apple M1/M2/M3 | ARM NEON | Excellent (with Metal) |
| Raspberry Pi 4/5 | ARM NEON | Usable for small models |

## GPU Support

### NVIDIA (CUDA)

| Requirement | Value |
|-------------|-------|
| Driver | 470+ |
| CUDA | 11.7+ |
| Container toolkit | nvidia-container-toolkit |

**Docker GPU setup**:
```yaml
deploy:
  resources:
    reservations:
      devices:
        - driver: nvidia
          count: all
          capabilities: [gpu]
```

### AMD (ROCm)

| Requirement | Value |
|-------------|-------|
| ROCm | 5.4+ |
| Image | `ollama/ollama:rocm` |

Supported GPUs: RX 6000/7000 series, MI series.

### Apple Metal

Native Metal support on macOS (not in Docker).

## Resource Requirements

### Memory (RAM)

Memory requirements depend on **model size** and **quantization**:

| Model Size | Q4_0 Quantization | Q8_0 Quantization | FP16 |
|------------|-------------------|-------------------|------|
| 1B params | ~1GB | ~2GB | ~4GB |
| 3B params | ~2GB | ~4GB | ~8GB |
| 7B params | ~4GB | ~8GB | ~16GB |
| 13B params | ~8GB | ~16GB | ~32GB |
| 70B params | ~40GB | ~80GB | ~140GB |

**Recommended minimum**: 4GB RAM (for tinyllama, phi-2)
**Recommended for 7B models**: 8GB+ RAM

### VRAM (GPU Memory)

Same requirements as RAM when using GPU acceleration. Models load into VRAM.

**Hybrid mode**: If model exceeds VRAM, Ollama splits between GPU and CPU.

### Disk

| Requirement | Value |
|-------------|-------|
| Models directory | `/root/.ollama` |
| Per model | 2-50GB depending on model |

## Popular Models

| Model | Size | RAM Needed | Use Case |
|-------|------|------------|----------|
| tinyllama | 1.1B | ~2GB | Minimal, testing |
| phi-2 | 2.7B | ~3GB | Coding, general |
| gemma:2b | 2B | ~2GB | General purpose |
| llama3.2:3b | 3B | ~3GB | General purpose |
| mistral | 7B | ~5GB | High quality |
| llama3.2 | 8B | ~6GB | Latest Meta model |
| codellama | 7B | ~5GB | Code generation |
| llama3.1:70b | 70B | ~40GB | Enterprise quality |

## Network Configuration

| Port | Protocol | Purpose |
|------|----------|---------|
| 11434 | HTTP | REST API |

## Health Check Strategy

### Selected Command
```yaml
healthcheck:
  test: ["CMD", "curl", "-f", "http://localhost:11434/api/tags"]
  interval: 30s
  timeout: 10s
  retries: 5
  start_period: 30s
```

**Why `/api/tags`**:
- Lists available models
- Returns 200 even with no models pulled
- Confirms API is responsive

### API Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/tags` | GET | List models |
| `/api/generate` | POST | Generate text |
| `/api/chat` | POST | Chat completion |
| `/api/pull` | POST | Download model |
| `/api/embeddings` | POST | Generate embeddings |

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `OLLAMA_HOST` | `127.0.0.1:11434` | Listen address |
| `OLLAMA_MODELS` | `~/.ollama/models` | Models directory |
| `OLLAMA_NUM_PARALLEL` | `1` | Parallel requests |
| `OLLAMA_GPU_OVERHEAD` | `0` | Reserved VRAM (bytes) |

## Compatibility Rules Analysis

### Pre-flight Checks

| Rule | Condition | Action | Rationale |
|------|-----------|--------|-----------|
| ARM32 | armv7l, armv6l | Fail | No images available |
| No GPU | requires_ai_any | Warning | CPU-only is slow |
| < 4GB VRAM | vram_mb_less_than | Warning | Limited models |
| < 4GB RAM | memory_mb_less_than | Fail | Most models need more |
| < 8GB RAM | memory_mb_less_than | Warning | 7B models need 8GB+ |
| Celeron J/N | processor_patterns | Warning | No AVX, very slow |

### Post-install Checks

| Pattern | Issue | Suggestion |
|---------|-------|------------|
| `Illegal instruction` | CPU incompatibility | Different hardware |
| `OOM\|out of memory` | Memory exhaustion | Smaller model |
| `CUDA.*error` | GPU init failed | Check drivers |
| `no suitable GPU` | No GPU detected | CPU mode |
| `model.*not found` | Download failed | Check network |

## Raspberry Pi Compatibility

| Device | Support | Notes |
|--------|---------|-------|
| Pi 5 (8GB) | ✅ | tinyllama, phi work well |
| Pi 4 (8GB) | ✅ | Small models only |
| Pi 4 (4GB) | ⚠️ | Very limited |
| Pi 3/2/Zero | ❌ | No ARM32 support |

**Pi Recommendations**:
- Use tinyllama (1.1B) or phi-2 (2.7B)
- Expect slow inference (~1-5 tokens/sec)
- Consider external inference server

## Security Considerations

| Concern | Mitigation |
|---------|------------|
| API exposed | Internal network (zen-garden) |
| Model downloads | Models from ollama.com verified |
| Resource exhaustion | Memory limits, model selection |

## Comparison with Alternatives

| Feature | Ollama | vLLM | LocalAI | text-generation-webui |
|---------|--------|------|---------|----------------------|
| Ease of use | ✅✅✅ | ✅ | ✅✅ | ✅✅ |
| API compatibility | Ollama | OpenAI | OpenAI | Multiple |
| GPU support | Good | Excellent | Good | Good |
| ARM support | ✅ | Limited | ✅ | ✅ |
| Model management | Built-in | Manual | Built-in | Built-in |
| Performance | Good | Excellent | Good | Good |

**For Zen Garden**: Ollama is recommended for simplicity and broad compatibility.

## OpenAI API Compatibility

Ollama supports OpenAI-compatible endpoints:

```bash
curl http://localhost:11434/v1/chat/completions \
  -H "Content-Type: application/json" \
  -d '{
    "model": "llama3.2",
    "messages": [{"role": "user", "content": "Hello!"}]
  }'
```

This enables drop-in replacement for OpenAI API clients.

## Validation Checklist

- [x] Docker image exists and is official
- [x] Multi-architecture support verified (amd64, arm64)
- [x] ARM32 limitation documented
- [x] CPU requirements documented (AVX beneficial)
- [x] GPU support documented (CUDA, ROCm)
- [x] Memory constraints documented
- [x] Model sizing documented
- [x] Health check command verified
- [x] MIT license confirmed

## Files

| File | Status |
|------|--------|
| `ollama.snippet.yaml` | ✅ Updated (version, OLLAMA_HOST) |
| `ollama.compatibility.yaml` | ✅ Updated (ARM32, warnings) |
| `ollama.frontmatter.json` | ✅ Updated (tags, gpu_recommended) |
| `ollama.research.md` | ✅ Created |

## References

1. [Ollama Official Website](https://ollama.com/)
2. [Ollama GitHub](https://github.com/ollama/ollama)
3. [Ollama Docker Hub](https://hub.docker.com/r/ollama/ollama)
4. [Ollama API Documentation](https://github.com/ollama/ollama/blob/main/docs/api.md)
5. [llama.cpp GitHub](https://github.com/ggerganov/llama.cpp)
6. [Ollama Model Library](https://ollama.com/library)
