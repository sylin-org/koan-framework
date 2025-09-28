// Vector storage is handled by enhanced .Save() method in PipelineDataExtensions
// to maintain framework consistency: one persistence verb for all storage types
//
// The Save() method automatically detects embeddings in the pipeline envelope
// and uses Data<T,K>.SaveWithVector() when present, falling back to standard
// entity save when no embeddings are found.
//
// This maintains the clean pipeline API: .Tokenize().Save() just works
// with transparent vector storage when embeddings are present.