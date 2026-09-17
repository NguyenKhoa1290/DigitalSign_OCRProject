import logging
import httpx
from app.config import get_settings

logger = logging.getLogger(__name__)


async def update_ocr_result(
    doc_id: str,
    ocr_payload: dict,
    token: str,
    use_service_token: bool = False,
) -> bool:
    """
    Gọi DocumentService PATCH /api/documents/{id}/ocr để cập nhật kết quả OCR.
    ocr_payload: { doc_number, title, issued_date, ocr_data_raw }
    """
    settings = get_settings()
    url = f"{settings.document_service_url}/api/documents/{doc_id}/ocr"

    headers = {
        "Content-Type": "application/json",
    }
    if use_service_token:
        headers["X-Service-Token"] = token
    else:
        headers["Authorization"] = f"Bearer {token}"

    try:
        async with httpx.AsyncClient(timeout=30.0) as client:
            resp = await client.patch(url, json=ocr_payload, headers=headers)

        if resp.status_code in (200, 204):
            logger.info("Cập nhật OCR thành công cho document %s", doc_id)
            return True
        else:
            logger.warning(
                "DocumentService trả về %d khi cập nhật OCR doc=%s: %s",
                resp.status_code, doc_id, resp.text,
            )
            return False
    except httpx.RequestError as e:
        logger.error("Lỗi kết nối DocumentService: %s", e)
        return False
