# Pandoc Rendering Container

This FastAPI sidecar converts Markdown into PDF using Pandoc 3.1.11 and XeTeX. The image pins Pandoc/LaTeX versions to ensure byte-stable PDFs and disables LaTeX shell escape for safety.

## Building & Running

```bash
docker build -t meridian-pandoc ./samples/S7.Meridian/containers/pandoc
docker run -p 7070:7070 meridian-pandoc
```

Alternatively use the provided compose file:

```bash
docker compose -f docker-compose.pandoc.yml up -d
```

## API

- `GET /healthz` – readiness probe.
- `POST /render` – accepts `{ "markdown": "# ...", "content_hash": "optional" }` and returns `{ "pdfBase64": "...", "hash": "..." }`.

The service strips dangerous LaTeX commands (e.g. `\write18`, `\input`) before invoking Pandoc.
