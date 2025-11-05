// ═══════════════════════════════════════════════════════════════════════════════
// OBSOLETE: VectorizationWorker.cs
// ═══════════════════════════════════════════════════════════════════════════════
//
// This file has been made obsolete by ARCH-0070: Attribute-Driven AI Embeddings.
//
// BEFORE (230 lines of manual embedding generation):
// - Polled "vectorization-queue" partition for Media items
// - Computed ContentSignature manually
// - Looked up EmbeddingCacheEntry for cache hits
// - Called Ai.Embed() manually on cache miss
// - Stored embeddings in cache manually
// - Called VectorData<Media>.SaveWithVector() manually
// - Updated VectorizedAt timestamp manually
// - Moved items from vectorization-queue to live partition
//
// AFTER (automatic embedding generation):
// - Media entity has [Embedding(Properties = new[] { ... })] attribute
// - Embedding generation happens automatically on media.Save()
// - ContentSignature tracked automatically in EmbeddingState<Media>
// - Cache handled by framework (EmbeddingCacheEntry deprecated)
// - VectorData<T>.SaveWithVector() called automatically
// - VectorizedAt tracked automatically in EmbeddingState<Media>
//
// MIGRATION PATH:
// - Remove VectorizationWorker from DI registration
// - Remove "vectorization-queue" partition logic from pipeline
// - Embeddings now generated during import/validation phase automatically
// - Partition-based pipeline can be simplified (no separate vectorization phase needed)
//
// For async/deferred embedding (Phase 3):
// - Use [Embedding(Async = true)] to queue for background processing
// - EmbedJob<Media> entity will track queued embedding jobs
// - EmbeddingWorker will process jobs from all async entities
//
// ═══════════════════════════════════════════════════════════════════════════════

// Original file moved to VectorizationWorker.OBSOLETE.cs for reference
// To restore manual embedding generation, refer to this file
// Delete this file after confirming automatic embedding works correctly
