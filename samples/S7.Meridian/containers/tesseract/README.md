# Meridian OCR Container

This lightweight FastAPI wrapper exposes the Tesseract OCR engine over HTTP for the Meridian pipeline.

## Usage

```bash
docker compose -f docker-compose.tesseract.yml up -d
```

The service listens on `http://localhost:6060/ocr` and expects a PDF payload using `multipart/form-data` with the field name `file`.

## Response Schema

```json
{
  "text": "<extracted text>",
  "confidence": 0.72,
  "pages": 12
}
```

Confidence is the average Tesseract token confidence normalised to a 0-1 range.
