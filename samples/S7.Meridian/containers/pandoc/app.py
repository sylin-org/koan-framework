import base64
import tempfile
from typing import Optional

import base64
import tempfile
from typing import Optional

import pypandoc
from fastapi import FastAPI, HTTPException
from pydantic import BaseModel

BLOCKED_TOKENS = ("\\write18", "\\input", "\\include", "\\openout", "\\read", "\\catcode")

app = FastAPI(title="Meridian Pandoc Renderer", version="1.0.0")


class RenderRequest(BaseModel):
    markdown: str
    content_hash: Optional[str] = None


class RenderResponse(BaseModel):
    pdfBase64: str
    hash: Optional[str] = None


def sanitize_markdown(markdown: str) -> str:
    sanitized_lines = []
    for line in markdown.splitlines():
        if any(token.lower() in line.lower() for token in BLOCKED_TOKENS):
            continue
        sanitized_lines.append(line)
    return "\n".join(sanitized_lines)


@app.get("/healthz")
async def health() -> dict[str, str]:
    return {"status": "ok"}


@app.post("/render", response_model=RenderResponse)
async def render(request: RenderRequest) -> RenderResponse:
    if not request.markdown or not request.markdown.strip():
        return RenderResponse(pdfBase64="", hash=request.content_hash)

    sanitized = sanitize_markdown(request.markdown)
    if not sanitized.strip():
        return RenderResponse(pdfBase64="", hash=request.content_hash)

    try:
        with tempfile.NamedTemporaryFile(suffix=".pdf") as pdf_file:
            pypandoc.convert_text(
                sanitized,
                to="pdf",
                format="md",
                outputfile=pdf_file.name,
                extra_args=["--pdf-engine=xelatex"],
            )
            pdf_file.seek(0)
            pdf_bytes = pdf_file.read()
    except RuntimeError as exc:  # Pandoc errors raise RuntimeError
        raise HTTPException(status_code=500, detail=str(exc)) from exc

    encoded = base64.b64encode(pdf_bytes).decode("utf-8")
    return RenderResponse(pdfBase64=encoded, hash=request.content_hash)
