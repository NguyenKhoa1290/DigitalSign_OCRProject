import logging
import httpx
from app.config import get_settings

logger = logging.getLogger(__name__)


class DocumentUnavailableError(RuntimeError):
    def __init__(self, message: str, status_code: int = 409):
        super().__init__(message)
        self.status_code = status_code


async def require_active_document(doc_id: str, minio_path: str, token: str, use_service_token: bool) -> str:
    """Reject deleted documents and stale Kafka events before downloading or running OCR."""
    settings = get_settings()
    headers = {"X-Service-Token": token} if use_service_token else {"Authorization": f"Bearer {token}"}
    try:
        async with httpx.AsyncClient(timeout=15.0) as client:
            response = await client.get(f"{settings.document_service_url}/api/documents/{doc_id}/ocr-state", headers=headers)
    except httpx.RequestError as exc:
        raise DocumentUnavailableError("Chưa kiểm tra được trạng thái công văn.", 503) from exc
    if response.status_code == 404:
        raise DocumentUnavailableError("Công văn không tồn tại hoặc đã bị xóa.", 404)
    if response.status_code != 200:
        raise DocumentUnavailableError("Chưa xác nhận được quyền và trạng thái công văn.", 503)
    try:
        state = response.json()["data"]
        stored_path = state["minioPath"]
        version = state["concurrencyVersion"]
        if not isinstance(stored_path, str) or not isinstance(version, str) or not version:
            raise DocumentUnavailableError("Trạng thái công văn không hợp lệ.", 503)
        if stored_path.removeprefix("documents/") != minio_path.removeprefix("documents/"):
            raise DocumentUnavailableError("Tệp công văn đã thay đổi. Bỏ qua yêu cầu OCR cũ.")
        return version
    except (KeyError, TypeError, ValueError) as exc:
        raise DocumentUnavailableError("Trạng thái công văn không hợp lệ.", 503) from exc


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
