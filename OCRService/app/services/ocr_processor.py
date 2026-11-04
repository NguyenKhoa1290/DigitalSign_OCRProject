import io
import logging
import os
import tempfile
from typing import Optional

from app.services.document_input import load_document_images

from app.ocr.engine import OcrEngine
from app.ocr.extractor import extract_fields
from app.services import document_service
from app.services.word_exporter import create_editable_docx, extract_document
from app.config import get_settings

logger = logging.getLogger(__name__)

# Engine singleton (load model một lần duy nhất)
_engine: Optional[OcrEngine] = None


async def export_word_from_upload(file_bytes: bytes, filename: str = "document.pdf") -> bytes:
    """Create an editable DOCX draft from a scanned PDF using PaddleOCR and optional local LLM."""
    with tempfile.TemporaryDirectory() as tmpdir:
        images = load_document_images(file_bytes, filename)
        all_lines = []
        for page_num, image in enumerate(images, start=1):
            image_path = os.path.join(tmpdir, f"export_{page_num}.png")
            image.save(image_path, "PNG")
            all_lines.extend(get_engine().extract_lines(image_path))
    fields = extract_fields(all_lines)
    structured = await extract_document(images, all_lines, fields)
    return create_editable_docx(structured)


def get_engine() -> OcrEngine:
    global _engine
    if _engine is None:
        s = get_settings()
        _engine = OcrEngine(language=s.ocr_language, use_gpu=s.ocr_use_gpu)
    return _engine


async def process_document(doc_id: str, minio_path: str, token: str) -> dict:
    """Retired automatic Paddle path, including already queued legacy commands."""
    raise document_service.DocumentUnavailableError(
        "OCR tự động sau upload đã dừng. Hãy chọn model và tối đa 5 trang tại chi tiết công văn.", 410
    )
