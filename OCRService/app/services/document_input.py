"""Decode supported OCR inputs and enforce the page limit before recognition/cloud calls."""
import io
from pathlib import Path

from PIL import Image, ImageOps
from pdf2image import convert_from_bytes, pdfinfo_from_bytes

MAX_OCR_PAGES = 5


class DocumentInputError(ValueError):
    pass


def select_pages(expression: str | None, total: int) -> list[int]:
    if not expression:
        if total > MAX_OCR_PAGES:
            raise DocumentInputError(f"Mỗi lượt OCR hỗ trợ tối đa {MAX_OCR_PAGES} trang. Tệp này có {total} trang; hãy chọn trang trước khi quét.")
        return list(range(1, total + 1))
    selected = set()
    import re
    for part in expression.replace("–", "-").split(","):
        match = re.fullmatch(r"\s*(\d+)(?:\s*-\s*(\d+))?\s*", part)
        if not match:
            raise DocumentInputError("Nhập số trang như 1-5 hoặc 1, 3, 8-10.")
        start, end = int(match[1]), int(match[2] or match[1])
        if start < 1 or end < start or end > total:
            raise DocumentInputError(f"Số trang phải nằm trong khoảng 1 đến {total}.")
        if end - start + 1 > MAX_OCR_PAGES:
            raise DocumentInputError("Mỗi lượt OCR hỗ trợ tối đa 5 trang.")
        selected.update(range(start, end + 1))
        if len(selected) > MAX_OCR_PAGES:
            raise DocumentInputError("Mỗi lượt OCR hỗ trợ tối đa 5 trang.")
    return sorted(selected)


def document_page_count(file_bytes: bytes, filename: str) -> int:
    try:
        if Path(filename.lower()).suffix == ".pdf":
            count = int(pdfinfo_from_bytes(file_bytes, timeout=30).get("Pages", 0))
            if count < 1:
                raise DocumentInputError("Tệp không có trang để xử lý.")
            return count
        load_document_images(file_bytes, filename)
        return 1
    except DocumentInputError:
        raise
    except Exception as exc:
        raise DocumentInputError("Không đọc được tệp ảnh/PDF đã tải lên.") from exc


def load_document_images(file_bytes: bytes, filename: str, dpi: int = 200, pages: str | None = None):
    extension = Path(filename.lower()).suffix
    try:
        if extension == ".pdf":
            total = int(pdfinfo_from_bytes(file_bytes, timeout=30).get("Pages", 0))
            if total < 1:
                raise DocumentInputError("Tệp không có trang để xử lý.")
            selected = select_pages(pages, total)
            images = []
            for number in selected:
                images.extend(convert_from_bytes(file_bytes, dpi=dpi, fmt="png", first_page=number, last_page=number, timeout=60))
            return images
        if extension in {".png", ".jpg", ".jpeg", ".webp"}:
            select_pages(pages, 1)
            with Image.open(io.BytesIO(file_bytes)) as source:
                if source.format not in {"PNG", "JPEG", "WEBP"}:
                    raise DocumentInputError("Chỉ hỗ trợ PDF, PNG, JPG/JPEG hoặc WEBP.")
                if getattr(source, "n_frames", 1) != 1:
                    raise DocumentInputError("Chỉ hỗ trợ ảnh tĩnh; hãy tách các khung ảnh trước khi quét.")
                return [ImageOps.exif_transpose(source).convert("RGB")]
        raise DocumentInputError("Chỉ hỗ trợ PDF, PNG, JPG/JPEG hoặc WEBP.")
    except DocumentInputError:
        raise
    except Exception as exc:
        raise DocumentInputError("Không đọc được tệp ảnh/PDF đã tải lên.") from exc
