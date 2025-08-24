Sora.Ai.Provider.Ollama — Technical reference

Contract
- Implements Sora.AI.Contracts against a local/remote Ollama runtime.
- Inputs: Prompt/Message contracts, tool-call specs, embedding requests.
- Outputs: Text/JSON responses, streaming tokens, embedding vectors.

Options
- BaseUrl (http://localhost:11434 by default), Model id, Timeout, Retries, TLS options.
- HTTP client policy (retries/backoff), MaxConcurrentRequests, Stream buffer size.

Behavior
- Uses Ollama HTTP API for generate/chat/embeddings; supports streaming when requested.
- Doesn’t own auth; if Ollama is exposed remotely, secure via network policies.

Error modes
- Connection refused/timeouts, model not found, invalid parameters, rate limiting.
- Partial streams on network interruption; surface OperationCanceled on cancellation.

Edge cases
- Large prompts exceeding Ollama limits; model not pulled; cold-start latency on first call.
- Tool-call JSON adherence; reject unsupported tool schemas.

Security
- Treat prompts/outputs as sensitive; redact logs; disable verbose logging by default.
- When exposed over network, require TLS and IP allow-lists.

References
- ./README.md
- ../Sora.AI/TECHNICAL.md
- ../Sora.AI.Contracts/TECHNICAL.md