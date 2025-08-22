# S5.Recs Dev Notes

Weaviate single-node: to avoid RAFT bootstrap loops causing shutdown, we set `CLUSTER_HOSTNAME=node1` in `docker/compose.yml`.
Ollama embeddings: ensure the `all-minilm` model exists. From host: `curl http://localhost:5083/api/tags` should include it; otherwise pull with `ollama pull all-minilm` inside the container.
Logging: Framework remains at Information; app category `S5.Recs` is Debug in Development.
Data dirs are mounted under `samples/S5.Recs/data` so containers persist state between restarts.
