import logging
import os
import tempfile
from urllib.parse import quote
from fastapi import APIRouter, HTTPException, Header, UploadFile, File, Query
from fastapi.responses import StreamingResponse
import io
from pydantic import BaseModel
from typing import Optional
import httpx

from app.services import ocr_processor
from app.services import gemini_service
from app.services.document_service import DocumentUnavailableError
from app.services.layout_exporter import create_layout_docx
from app.services.document_input import load_document_images, DocumentInputError

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
    - Tải PDF hoặc ảnh từ MinIO theo `minio_path`; tối đa 5 trang
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
    except DocumentUnavailableError as exc:
        raise HTTPException(status_code=exc.status_code, detail=str(exc)) from exc
    except DocumentInputError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    except Exception as e:
        logger.error("Lỗi OCR doc=%s: %s", request.doc_id, e, exc_info=True)
        raise HTTPException(status_code=500, detail=f"Lỗi OCR: {str(e)}")


@router.post(
    "/process-upload",
    response_model=OcrResponse,
    summary="Upload PDF/ảnh và chạy OCR trực tiếp (tối đa 5 trang)",
)
async def process_upload(
    doc_id: str = "test-doc",
    file: UploadFile = File(...),
):
    """
    Upload PDF hoặc ảnh và chạy OCR ngay, tối đa 5 trang — không cần MinIO.
    """
    from app.ocr.extractor import extract_fields

    try:
        pdf_bytes = await file.read()

        with tempfile.TemporaryDirectory() as tmpdir:
            images = load_document_images(pdf_bytes, file.filename or "")
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
    except DocumentInputError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    except Exception as e:
        logger.error("Lỗi OCR upload: %s", e, exc_info=True)
        raise HTTPException(status_code=500, detail=str(e))


@router.post("/export-word-upload", summary="Xuất Word nháp từ PDF/ảnh, tối đa 5 trang")
async def export_word_upload(file: UploadFile = File(...)):
    try:
        docx_bytes = await ocr_processor.export_word_from_upload(await file.read(), file.filename or "")
        filename = f"ocr-draft-{(file.filename or 'document').rsplit('.', 1)[0]}.docx"
        return StreamingResponse(io.BytesIO(docx_bytes), media_type="application/vnd.openxmlformats-officedocument.wordprocessingml.document", headers={"Content-Disposition": f"attachment; filename=ocr-draft.docx; filename*=UTF-8''{quote(filename, safe='')}"})
    except DocumentInputError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc
    except Exception as exc:
        logger.error("Word export failed: %s", exc, exc_info=True)
        raise HTTPException(status_code=500, detail=str(exc))


@router.get("/gemini/models", summary="Danh sách model Gemini khả dụng")
async def gemini_models():
    try:
        return await gemini_service.list_gemini_models()
    except RuntimeError as exc:
        raise HTTPException(status_code=503, detail=str(exc)) from exc
    except httpx.HTTPStatusError as exc:
        logger.warning("Google model-list request failed with HTTP %s", exc.response.status_code)
        status = 429 if exc.response.status_code == 429 else 503
        raise HTTPException(status_code=status, detail="Không tải được danh sách model Gemini. Kiểm tra khóa ở trang Admin.") from exc
    except httpx.RequestError as exc:
        raise HTTPException(status_code=503, detail="Không kết nối được Google AI Studio.") from exc


@router.post("/gemini/export-word-upload", summary="Dựng Word từ bản quét bằng PaddleOCR và Gemini")
async def gemini_export_word_upload(
    file: UploadFile = File(...),
    model_id: str = Query(..., min_length=5, max_length=100),
):
    file_bytes = await file.read(25 * 1024 * 1024 + 1)
    if len(file_bytes) > 25 * 1024 * 1024:
        raise HTTPException(status_code=413, detail="Tệp vượt quá giới hạn 25 MB.")
    try:
        images = load_document_images(file_bytes, file.filename or "", dpi=180)
    except DocumentInputError as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc

    try:
        api_key = await gemini_service.get_gemini_api_key()
        models = await gemini_service.list_gemini_models(api_key)
        if model_id not in {model["id"] for model in models}:
            raise HTTPException(status_code=400, detail="Model Gemini không nằm trong danh sách khả dụng.")

        layouts = []
        engine = ocr_processor.get_engine()
        with tempfile.TemporaryDirectory() as tmpdir:
            for page_number, image in enumerate(images, start=1):
                image_path = os.path.join(tmpdir, f"page-{page_number}.png")
                image.save(image_path, "PNG")
                lines = engine.extract_lines(image_path)
                layouts.append(await gemini_service.generate_layout(image, lines, model_id, api_key))

        combined = {**layouts[0], "blocks": []}
        for page_number, layout in enumerate(layouts):
            if page_number:
                combined["blocks"].append({"type": "page_break"})
            combined["blocks"].extend(layout["blocks"])
        docx_bytes = create_layout_docx(combined)
        headers = {"Content-Disposition": 'attachment; filename="ban-nhap-ocr-gemini.docx"'}
        return StreamingResponse(io.BytesIO(docx_bytes), media_type="application/vnd.openxmlformats-officedocument.wordprocessingml.document", headers=headers)
    except HTTPException:
        raise
    except gemini_service.GeminiRequestError as exc:
        raise HTTPException(status_code=exc.status_code, detail=str(exc)) from exc
    except httpx.HTTPStatusError as exc:
        logger.warning("Gemini OCR request failed with HTTP %s", exc.response.status_code)
        status = 429 if exc.response.status_code == 429 else 503
        message = "Google AI Studio hết hạn mức. Hãy chọn model Gemini khác." if status == 429 else "Không gọi được Google AI Studio. Hãy thử model khác hoặc thử lại sau."
        raise HTTPException(status_code=status, detail=message) from exc
    except httpx.RequestError as exc:
        raise HTTPException(status_code=503, detail="Không kết nối được dịch vụ cấu hình hoặc Google AI Studio.") from exc
    except Exception as exc:
        logger.error("Gemini Word export failed (%s)", type(exc).__name__)
        raise HTTPException(status_code=500, detail="Không thể tạo Word từ bản quét.") from exc


@router.get("/health", summary="Health check")
async def health():
    return {"status": "ok", "service": "OCR Service"}
