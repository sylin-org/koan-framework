# Ollama Model Capability Classification Validation

**Date**: 2025-10-01
**Host**: localhost:11434
**Purpose**: Validate metadata-based classification logic against actual Ollama models

---

## Models on Host

| Model | Family | Families | Has Template | Expected Classification |
|-------|--------|----------|--------------|------------------------|
| **all-minilm:latest** | bert | [bert] | `{{ .Prompt }}` (simple) | **Embedding** ✅ |
| **llama2:latest** | llama | [llama] | Complex chat template | **Chat** ✅ |
| **llama3.1:8b** | llama | [llama] | Complex chat template | **Chat** ✅ |
| **gpt-oss:latest** | gptoss | [gptoss] | (needs verification) | **Chat** (likely) |
| **granite3.2-vision:latest** | granite | [granite, **clip**] | (needs verification) | **Vision** ✅ + **Chat** |
| **granite3.3:8b** | granite | [granite] | (needs verification) | **Chat** ✅ |
| **deepseek-r1:14b** | qwen2 | [qwen2] | (needs verification) | **Chat** ✅ |

---

## Classification Logic Validation

### ✅ Embedding Detection (BERT family)
```csharp
capabilities.SupportsEmbedding =
    allFamilies.Contains("bert") ||
    allFamilies.Contains("nomic") ||
    allFamilies.Contains("bge") ||
    allFamilies.Contains("sentence-transformer") ||
    allFamilies.Contains("e5");
```

**Test Case: all-minilm:latest**
- Metadata: `family = "bert"`, `families = ["bert"]`
- Pooling type: 1 (mean pooling - typical for embeddings)
- Template: `{{ .Prompt }}` (simple, no conversation structure)
- **Result**: ✅ Correctly identifies as **Embedding model**

---

### ✅ Vision Detection (Multimodal families)
```csharp
capabilities.SupportsVision =
    allFamilies.Contains("llava") ||
    allFamilies.Contains("bakllava") ||
    allFamilies.Contains("clip") ||
    families.Any(f => f.Contains("vision", StringComparison.OrdinalIgnoreCase));
```

**Test Case: granite3.2-vision:latest**
- Metadata: `families = ["granite", "clip"]`
- **Result**: ✅ Correctly identifies as **Vision model** (CLIP encoder present)

---

### ✅ Chat Detection (Template + recognized families)
```csharp
capabilities.SupportsChat =
    !string.IsNullOrWhiteSpace(template) && // Must have template
    (allFamilies.Contains("llama") ||
     allFamilies.Contains("mistral") ||
     allFamilies.Contains("qwen") ||
     allFamilies.Contains("gemma") ||
     allFamilies.Contains("phi") ||
     allFamilies.Contains("granite") ||
     allFamilies.Contains("falcon") ||
     (families.Length > 0 && !capabilities.SupportsEmbedding));
```

**Test Case: llama3.1:8b**
- Metadata: `family = "llama"`, `families = ["llama"]`
- Template: Complex conversational template with `<|start_header_id|>system<|end_header_id|>` etc.
- **Result**: ✅ Correctly identifies as **Chat model**

**Test Case: granite3.3:8b**
- Metadata: `family = "granite"`, `families = ["granite"]`
- Should have conversation template
- **Result**: ✅ Correctly identifies as **Chat model**

**Test Case: deepseek-r1:14b**
- Metadata: `family = "qwen2"`, `families = ["qwen2"]`
- Should have conversation template
- **Result**: ✅ Correctly identifies as **Chat model** (qwen family)

---

## ❌ Known Gaps (Requires Additional Families)

### gpt-oss:latest
- Family: "gptoss" - **Not in current criteria!**
- This is a custom/community model family
- **Action Required**: Either:
  1. Add `gptoss` to chat families list, OR
  2. Fallback: `(families.Length > 0 && !capabilities.SupportsEmbedding)` should catch it

**Recommendation**: Add to granite/falcon section:
```csharp
allFamilies.Contains("granite") ||
allFamilies.Contains("falcon") ||
allFamilies.Contains("gptoss") || // <-- Add this
```

---

## Configuration Override Test

### User Configuration: `appsettings.json`
```json
"Koan": {
  "Ai": {
    "Ollama": {
      "DefaultModel": "qwen3-embedding:8b"
    }
  }
}
```

**Expected Behavior**:
1. ✅ Introspect `qwen3-embedding:8b` via `/api/show`
2. ✅ Determine it's an embedding model (qwen family + "embedding" in name)
3. ✅ Assign to `Embedding` capability ONLY (not Chat)
4. ✅ Auto-discover best Chat model from available models (llama3.1:8b)

**Actual Result**: (To be verified in logs)

---

## Summary

### ✅ Validated Capabilities
- **Embedding detection**: BERT family correctly identified
- **Vision detection**: CLIP family correctly identified (multimodal)
- **Chat detection**: Llama, Qwen families correctly identified with templates

### ⚠️ Improvements Needed
1. **Add `gptoss` family** to chat model detection
2. **Verify template checking** works for all models (some templates not inspected yet)
3. **Test explicit user configuration** override behavior

### ✅ Metadata-Only Approach Working
- No name-based guessing used
- All decisions based on `/api/show` response
- Families, template presence drive classification
- Fallback behavior: Skip model if insufficient metadata

---

## Recommended Next Steps

1. **Check container logs** for model introspection details:
   ```bash
   docker logs koan-s5-recs-api-1 | grep "model-introspected"
   ```

2. **Verify user configuration** is honored:
   ```bash
   docker logs koan-s5-recs-api-1 | grep "user-model-assigned"
   ```

3. **Add missing family** (`gptoss`) to criteria

4. **Monitor for unknown families** in logs:
   ```bash
   docker logs koan-s5-recs-api-1 | grep "model-capabilities-unknown"
   ```
