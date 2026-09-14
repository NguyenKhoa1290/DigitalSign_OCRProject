import io
import json
import logging
import os
import tempfile
from datetime import date
from typing import Optional

from pdf2image import convert_from_bytes

from app.ocr.engine import OcrEngine
from app.ocr.extractor import extract_fields
from app.services import minio_service, document_service
from app.config import get_settings

logger = logging.getLogger(__name__)

# Engine singleton (load model một lần duy nhất)
_engine: Optional[OcrEngine] = None


def get_engine() -> OcrEngine:
    global _engine
    if _engine is None:
        s = get_settings()
        _engine = OcrEngine(language=s.ocr_language, use_gpu=s.ocr_use_gpu)
    return _engine


async def process_document(doc_id: str, minio_path: str, token: str) -> dict:
    """
    Pipeline OCR hoàn chỉnh:
    1. Tải PDF từ MinIO
    2. Chuyển PDF → ảnh (pdf2image)
    3. Chạy PaddleOCR từng trang
    4. Bóc tách các trường: doc_number, title, issued_date
    5. Gọi DocumentService cập nhật kết quả
    6. Trả về kết quả
    """
    settings = get_settings()

    # 1. Tải PDF
    logger.info("Bắt đầu OCR cho document: %s (path=%s)", doc_id, minio_path)
    pdf_bytes = minio_service.download_file(minio_path)

    # 2. Chuyển PDF → ảnh
    with tempfile.TemporaryDirectory() as tmpdir:
        images = convert_from_bytes(pdf_bytes, dpi=200, fmt="png")
        logger.info("PDF có %d trang, bắt đầu nhận diện...", len(images))

        all_lines = []
        raw_pages = []

        for page_num, image in enumerate(images, start=1):
            img_path = os.path.join(tmpdir, f"page_{page_num}.png")
            image.save(img_path, "PNG")

            # 3. Chạy OCR
            engine = get_engine()
            lines = engine.extract_lines(img_path)
            all_lines.extend(lines)

            raw_pages.append({
                "page": page_num,
                "lines": lines,
            })
            logger.info("Trang %d: nhận diện %d dòng text", page_num, len(lines))

    # 4. Bóc tách trường
    fields = extract_fields(all_lines)

    # Chuyển issued_date thành string ISO
    issued_date_str: Optional[str] = None
    if isinstance(fields.get("issued_date"), date):
        issued_date_str = fields["issued_date"].isoformat()

    # 5. Chuẩn bị payload gửi DocumentService
    ocr_payload = {
        "docNumber": fields.get("doc_number"),
        "title": fields.get("title"),
        "issuedDate": issued_date_str,
        "ocrDataRaw": json.dumps(
            {
                "pages": raw_pages,
                "extracted": {
                    "doc_number": fields.get("doc_number"),
                    "issued_date": issued_date_str,
                    "title": fields.get("title"),
                    "issuing_org": fields.get("issuing_org"),
                },
            },
            ensure_ascii=False,
        ),
    }

    # 6. Gọi DocumentService
    if token:
        await document_service.update_ocr_result(doc_id, ocr_payload, token)
    else:
        logger.warning("Không có token, bỏ qua bước cập nhật DocumentService.")

    logger.info(
        "OCR hoàn tất: doc=%s | doc_number=%s | date=%s",
        doc_id,
        fields.get("doc_number"),
        issued_date_str,
    )

    return {
        "doc_id": doc_id,
        "doc_number": fields.get("doc_number"),
        "title": fields.get("title"),
        "issued_date": issued_date_str,
        "issuing_org": fields.get("issuing_org"),
        "total_pages": len(images),
        "total_lines": len(all_lines),
    }
