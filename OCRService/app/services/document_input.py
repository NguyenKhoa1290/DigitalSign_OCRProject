"""Decode supported OCR inputs and enforce the page limit before recognition/cloud calls."""
import io
from pathlib import Path

from PIL import Image, ImageOps
from pdf2image import convert_from_bytes, pdfinfo_from_bytes

MAX_OCR_PAGES = 5


class DocumentInputError(ValueError):
    pass


def load_document_images(file_bytes: bytes, filename: str, dpi: int = 200):
    extension = Path(filename.lower()).suffix
    try:
        if extension == ".pdf":
            pages = int(pdfinfo_from_bytes(file_bytes, timeout=30).get("Pages", 0))
            if pages > MAX_OCR_PAGES:
                raise DocumentInputError(f"Mỗi lượt OCR hỗ trợ tối đa {MAX_OCR_PAGES} trang. Tệp này có {pages} trang; hãy tách tài liệu trước khi quét.")
            if pages < 1:
                raise DocumentInputError("Tệp không có trang để xử lý.")
            return convert_from_bytes(file_bytes, dpi=dpi, fmt="png", first_page=1, last_page=pages, timeout=60)
        if extension in {".png", ".jpg", ".jpeg", ".webp"}:
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
