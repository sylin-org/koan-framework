from fastapi import FastAPI, File, HTTPException, UploadFile
from fastapi.responses import JSONResponse
from pdf2image import convert_from_bytes
import pytesseract
from pytesseract import Output
from typing import List

app = FastAPI(title="Meridian Tesseract Service", version="1.0.0")


def _estimate_confidence(image) -> float:
    data = pytesseract.image_to_data(image, output_type=Output.DICT)
    confidences: List[float] = []
    for value in data.get("conf", []):
        try:
            conf = float(value)
        except (TypeError, ValueError):
            continue
        if conf >= 0:
            confidences.append(conf)
    if not confidences:
        return 0.0
    return sum(confidences) / (len(confidences) * 100.0)


@app.post("/ocr")
async def perform_ocr(file: UploadFile = File(...)):
    if file.content_type not in {"application/pdf", "application/octet-stream", "pdf"}:
        raise HTTPException(status_code=415, detail="Only PDF uploads are supported")

    payload = await file.read()
    if not payload:
        raise HTTPException(status_code=400, detail="Uploaded document was empty")

    try:
        images = convert_from_bytes(payload, dpi=300)
    except Exception as exc:  # pragma: no cover - best effort logging
        raise HTTPException(status_code=400, detail=f"Failed to convert PDF to images: {exc}") from exc

    if not images:
        return JSONResponse({"text": "", "confidence": 0.0, "pages": 0})

    text_segments: List[str] = []
    confidences: List[float] = []

    for image in images:
        text_segments.append(pytesseract.image_to_string(image))
        confidences.append(_estimate_confidence(image))

    combined = "\n".join(segment.strip() for segment in text_segments if segment).strip()
    confidence = sum(confidences) / len(confidences) if confidences else 0.0

    return JSONResponse(
        {
            "text": combined,
            "confidence": round(confidence, 4),
            "pages": len(images),
        }
    )
