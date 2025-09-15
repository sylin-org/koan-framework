# Tests guide

This repository includes a mix of unit tests and integration tests. Some integration tests rely on Docker or optional app dependencies. They are designed to be safe-by-default on local machines.

- Redis integration tests

  - Use an existing REDIS instance if any of these env vars are set: `Koan_REDIS__CONNECTION_STRING`, `REDIS_URL`, or `REDIS_CONNECTION_STRING`.
  - Otherwise, they will attempt to start a temporary Docker container (`redis:7-alpine`). On platforms where Docker attach/hijack is unavailable, they will automatically skip.

- Weaviate integration tests

  - Attempt to start a temporary Weaviate container (`semitechnologies/weaviate`) using a unique container name and a dynamic host port.
  - If Docker is unavailable or the container fails to start, tests are skipped automatically.

- S4 Web integration tests (GraphQL)
  - These are disabled by default to avoid optional dependency churn. Enable them explicitly by setting the environment variable:
    - Windows PowerShell:
      - `$env:Koan_ENABLE_S4_TESTS = '1'`
    - Bash:
      - `export Koan_ENABLE_S4_TESTS=1`

Notes

- All other tests should run without special setup. If Docker Desktop is installed and running, the Docker-backed tests will execute; otherwise they will be skipped.
