import logging
from fastapi import APIRouter, HTTPException, Header, UploadFile, File
from pydantic import BaseModel
from typing import Optional

from app.services import ocr_processor

logger = logging.getLogger(__name__)
router = APIRouter(prefix="/api/ocr", tags=["OCR"])


class OcrRequest(BaseModel):
    doc_id: str
    minio_path: str   # objectName trên MinIO, ví dụ: "abc123.pdf"
    token: str = ""   # JWT người dùng; nếu rỗng sẽ fallback sang SERVICE_TOKEN


class OcrResponse(BaseModel):
    doc_id: str
    doc_number: Optional[str] = None
    title: Optional[str] = None
    issued_date: Optional[str] = None
    issuing_org: Optional[str] = None
    total_pages: int = 0
    total_lines: int = 0
    message: str = "Thành công"


@router.post("/process", response_model=OcrResponse, summary="Kích hoạt OCR cho công văn")
async def process_ocr(request: OcrRequest):
    """
    Kích hoạt OCR thủ công cho một công văn.
    - Tải PDF từ MinIO theo `minio_path`
    - Chạy PaddleOCR bóc tách: số hiệu, ngày, trích yếu
    - Gọi DocumentService cập nhật kết quả bằng JWT hoặc SERVICE_TOKEN
    """
    try:
        result = await ocr_processor.process_document(
            doc_id=request.doc_id,
            minio_path=request.minio_path,
            token=request.token,
        )
        return OcrResponse(**result, message="OCR hoàn tất thành công.")
    except Exception as e:
        logger.error("Lỗi OCR doc=%s: %s", request.doc_id, e, exc_info=True)
        raise HTTPException(status_code=500, detail=f"Lỗi OCR: {str(e)}")


@router.post(
    "/process-upload",
    response_model=OcrResponse,
    summary="Upload PDF và chạy OCR trực tiếp (test)",
)
async def process_upload(
    doc_id: str = "test-doc",
    file: UploadFile = File(...),
):
    """
    Upload file PDF và chạy OCR ngay — dùng để test nhanh không cần MinIO.
    """
    import tempfile, os
    from pdf2image import convert_from_bytes
    from app.ocr.engine import OcrEngine
    from app.ocr.extractor import extract_fields
    import json

    try:
        pdf_bytes = await file.read()
        settings_obj = __import__("app.config", fromlist=["get_settings"]).get_settings()

        with tempfile.TemporaryDirectory() as tmpdir:
            from pdf2image import convert_from_bytes
            images = convert_from_bytes(pdf_bytes, dpi=200, fmt="png")
            engine = ocr_processor.get_engine()
            all_lines = []
            for i, img in enumerate(images, 1):
                img_path = os.path.join(tmpdir, f"p{i}.png")
                img.save(img_path, "PNG")
                all_lines.extend(engine.extract_lines(img_path))

        fields = extract_fields(all_lines)
        issued_date_str = fields["issued_date"].isoformat() if fields.get("issued_date") else None

        return OcrResponse(
            doc_id=doc_id,
            doc_number=fields.get("doc_number"),
            title=fields.get("title"),
            issued_date=issued_date_str,
            issuing_org=fields.get("issuing_org"),
            total_pages=len(images),
            total_lines=len(all_lines),
            message="OCR hoàn tất (test upload).",
        )
    except Exception as e:
        logger.error("Lỗi OCR upload: %s", e, exc_info=True)
        raise HTTPException(status_code=500, detail=str(e))


@router.get("/health", summary="Health check")
async def health():
    return {"status": "ok", "service": "OCR Service"}
