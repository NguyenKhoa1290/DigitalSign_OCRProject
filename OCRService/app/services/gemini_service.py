import base64
import io
import json
import logging
from typing import Any

import httpx

from app.config import get_settings

logger = logging.getLogger(__name__)

PROMPT_VERSION = "image-layout-v3-vi-grid-counting"
PROMPT = """Bạn là chuyên viên số hóa văn bản hành chính tiếng Việt. Hãy dựng lại trang trong ảnh thành bố cục Word có thể chỉnh sửa, tuân thủ schema JSON. Ảnh là nguồn chính; dòng chữ và tọa độ PaddleOCR là bằng chứng bổ trợ, có thể sai. Không tóm tắt, viết lại, bịa hoặc điền nội dung vào ô trống. Bỏ qua nền, tay, bóng và méo phối cảnh; ghi nội dung không đọc chắc chắn vào uncertain_fields.

Giữ thứ tự đọc và bố cục. Dùng paragraph cho đoạn toàn chiều rộng, columns cho các vùng chữ song song trong cùng dải ngang, table cho bảng có đường kẻ. Không giả lập cột bằng tab hoặc nhiều dấu cách. Giữ in đậm, in nghiêng và căn lề khi nhìn thấy.

Đếm đường biên ngang đi qua các cột để xác định hàng, bỏ qua baseline chữ, bóng và mép ngoài bảng. Tiêu đề nhiều dòng vẫn là một hàng. Với lưới không gộp ô, H đường biên ngang tạo H-1 hàng tổng; nếu có một hàng tiêu đề thì số hàng thân là H-2. Chỉ dùng công thức khi ảnh xác nhận lưới đó. Ghi từng hàng thân đúng một lần: hàng có chữ trong rows, hàng trống trong blank_rows. Nếu toàn thân trống, rows=[] và blank_rows bằng số hàng thân quan sát được. Không đổi số hàng để lấp trang.

Giữ nguyên tên, ngày tháng, dấu câu và số trang. Không tạo chữ ký hoặc nội dung con dấu bị che. Tuân thủ số cột: mọi hàng trong rows phải có đúng số ô bằng headers; số widths phải khớp số cột. Các chuỗi xuống dòng JSON phải hiển thị thành xuống dòng thật. Không thêm lời nhận xét/nhãn OCR vào thân tài liệu. Dùng Times New Roman khi phù hợp và ghi điểm chưa chắc chắn bằng tiếng Việt trong uncertain_fields.
"""


def _object(properties: dict[str, Any], required: list[str]) -> dict[str, Any]:
    return {"type": "object", "properties": properties, "required": required, "additionalProperties": False}


def _array(items: dict[str, Any]) -> dict[str, Any]:
    return {"type": "array", "items": items}


def layout_schema() -> dict[str, Any]:
    string = {"type": "string"}
    number = {"type": "number", "minimum": 0, "maximum": 100}
    paragraph = _object({
        "type": {"type": "string", "enum": ["paragraph"]}, "text": string,
        "align": {"type": "string", "enum": ["left", "center", "right", "justify"]},
        "bold": {"type": "boolean"}, "italic": {"type": "boolean"},
        "font_size": {"type": "number", "minimum": 9, "maximum": 24},
        "space_before_pt": {"type": "number", "minimum": 0, "maximum": 72},
        "space_after_pt": {"type": "number", "minimum": 0, "maximum": 72},
        "line_spacing": {"type": "number", "minimum": 1, "maximum": 2},
        "keep_with_next": {"type": "boolean"},
        "runs": _array(_object({"text": string, "bold": {"type": "boolean"}, "italic": {"type": "boolean"}}, ["text"])),
    }, ["type", "text"])
    columns = _object({
        "type": {"type": "string", "enum": ["columns"]},
        "widths": _array(number), "columns": _array(_array(paragraph)),
    }, ["type", "widths", "columns"])
    table = _object({
        "type": {"type": "string", "enum": ["table"]},
        "headers": _array(string), "column_widths": _array(number),
        "rows": _array(_array(string)),
        "blank_rows": {"type": "integer", "minimum": 0, "maximum": 100},
        "header_height_cm": {"type": "number", "minimum": 0.2, "maximum": 4},
        "row_height_cm": {"type": "number", "minimum": 0.2, "maximum": 4},
    }, ["type", "headers", "column_widths", "rows", "blank_rows", "header_height_cm", "row_height_cm"])
    page = {f"margin_{edge}_cm": {"type": "number", "minimum": 0.5, "maximum": 4}
            for edge in ("top", "bottom", "left", "right")}
    page.update({"width_cm": {"type": "number", "minimum": 15, "maximum": 30},
                 "height_cm": {"type": "number", "minimum": 20, "maximum": 45}})
    return _object({
        "page": _object(page, list(page)), "font_name": string,
        "font_size": {"type": "number", "minimum": 9, "maximum": 18},
        "blocks": _array({"anyOf": [paragraph, columns, table]}),
        "uncertain_fields": _array(string),
    }, ["page", "font_name", "font_size", "blocks", "uncertain_fields"])


async def get_gemini_api_key() -> str:
    settings = get_settings()
    if not settings.service_token:
        raise RuntimeError("OCR service token is not configured")
    async with httpx.AsyncClient(timeout=10) as client:
        response = await client.get(
            settings.identity_service_url.rstrip("/") + "/api/internal/gemini-settings",
            headers={"X-OCR-Service-Token": settings.service_token},
        )
    if response.status_code == 404:
        raise RuntimeError("Gemini API key chưa được Admin cấu hình.")
    response.raise_for_status()
    api_key = response.json().get("apiKey", "")
    if not api_key:
        raise RuntimeError("Gemini API key chưa được Admin cấu hình.")
    return api_key


async def list_gemini_models(api_key: str | None = None) -> list[dict[str, str]]:
    key = api_key or await get_gemini_api_key()
    async with httpx.AsyncClient(timeout=20) as client:
        response = await client.get(
            "https://generativelanguage.googleapis.com/v1beta/models",
            headers={"x-goog-api-key": key},
        )
    response.raise_for_status()
    models = []
    for model in response.json().get("models", []):
        name = model.get("name", "")
        methods = model.get("supportedGenerationMethods", [])
        if not name.startswith("models/gemini-") or "generateContent" not in methods:
            continue
        model_id = name.removeprefix("models/")
        if any(term in model_id.lower() for term in ("embedding", "tts", "live")):
            continue
        models.append({"id": model_id, "displayName": model.get("displayName") or model_id})
    return sorted(models, key=lambda model: model["id"])


async def generate_layout(image, lines: list[dict], model_id: str, api_key: str) -> dict:
    image = image.convert("RGB")
    image.thumbnail((1800, 1800))
    buffer = io.BytesIO()
    image.save(buffer, "JPEG", quality=92)
    evidence = [{"text": line["text"], "confidence": line["confidence"], "box": line["box"]}
                for line in lines]
    schema = layout_schema()
    payload = {
        "systemInstruction": {"parts": [{"text": PROMPT}]},
        "contents": [{"role": "user", "parts": [
            {"text": "Hãy dựng lại đúng trang tài liệu trong ảnh. Bằng chứng PaddleOCR:\n" +
             json.dumps(evidence, ensure_ascii=False, separators=(",", ":"))},
            {"inlineData": {"mimeType": "image/jpeg", "data": base64.b64encode(buffer.getvalue()).decode("ascii")}},
        ]}],
        "generationConfig": {
            "temperature": 0,
            "maxOutputTokens": 12000,
            "responseMimeType": "application/json",
            "responseJsonSchema": schema,
            "thinkingConfig": {"thinkingLevel": "medium"},
        },
    }
    async with httpx.AsyncClient(timeout=300) as client:
        response = await client.post(
            f"https://generativelanguage.googleapis.com/v1beta/models/{model_id}:generateContent",
            headers={"x-goog-api-key": api_key}, json=payload,
        )
    if response.status_code == 429:
        raise GeminiRequestError(429, "Google AI Studio hết hạn mức cho model đã chọn. Hãy chọn model Gemini khác rồi thử lại.")
    if response.status_code == 503:
        raise GeminiRequestError(503, "Model Gemini đang quá tải. Hãy chọn model khác hoặc thử lại sau.")
    if response.status_code in (400, 401, 403):
        raise GeminiRequestError(response.status_code, "Google AI Studio từ chối yêu cầu. Kiểm tra API key, quyền và model đã chọn.")
    response.raise_for_status()
    data = response.json()
    try:
        parts = data["candidates"][0]["content"]["parts"]
        text = "".join(part.get("text", "") for part in parts if not part.get("thought"))
        layout = json.loads(text)
    except (KeyError, IndexError, TypeError, json.JSONDecodeError) as exc:
        raise GeminiRequestError(502, "Gemini không trả về layout JSON hợp lệ.") from exc
    validate_layout(layout)
    return layout


def validate_layout(layout: dict) -> None:
    if not isinstance(layout, dict) or not layout.get("blocks"):
        raise GeminiRequestError(502, "Gemini trả về bố cục trống.")
    for block in layout["blocks"]:
        kind = block.get("type")
        if kind == "table":
            headers = block.get("headers", [])
            widths = block.get("column_widths", [])
            rows = block.get("rows", [])
            if not headers or len(headers) != len(widths) or any(len(row) != len(headers) for row in rows):
                raise GeminiRequestError(502, "Bảng Gemini trả về sai số cột; chưa xuất Word.")
        elif kind == "columns":
            if len(block.get("widths", [])) != len(block.get("columns", [])):
                raise GeminiRequestError(502, "Gemini trả về sai số cột bố cục; chưa xuất Word.")
        elif kind != "paragraph":
            raise GeminiRequestError(502, "Gemini trả về loại khối bố cục không hỗ trợ.")


class GeminiRequestError(Exception):
    def __init__(self, status_code: int, message: str):
        super().__init__(message)
        self.status_code = status_code
