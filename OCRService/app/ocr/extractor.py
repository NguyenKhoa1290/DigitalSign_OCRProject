import re
import logging
from datetime import date
from typing import Optional

logger = logging.getLogger(__name__)


# ──────────────────────────────────────────────────────────────────────────────
# Patterns nhận diện trường thông tin công văn tiếng Việt
# ──────────────────────────────────────────────────────────────────────────────

# Số hiệu: 123/QĐ-HAU, 456/CV-CNTT, 01/TB-BGH ...
_DOC_NUMBER_PATTERN = re.compile(
    r"\b(\d{1,4})\s*/\s*([A-ZĐÁÀẢÃẠĂẮẶẶẴẨẤẦẨẪẬÉÈẺẼẸÊẾỀỂỄỆÍÌỈĨỊÓÒỎÕỌÔỐỒỔỖỘƠỚỜỞỠỢÚÙỦŨỤƯỨỪỬỮỰÝỲỶỸỴ]{2,20}[-–]\s*[A-ZĐÁÀẢÃẠĂẮẶẶẴẨẤẦẨẪẬÉÈẺẼẸÊẾỀỂỄỆÍÌỈĨỊÓÒỎÕỌÔỐỒỔỖỘƠỚỜỞỠỢÚÙỦŨỤƯỨỪỬỮỰÝỲỶỸỴ]{2,10})\b",
    re.IGNORECASE | re.UNICODE,
)

# Ngày ban hành: ngày 01 tháng 9 năm 2024 / 01/09/2024 / 01-09-2024
_DATE_PATTERN = re.compile(
    r"(?:ngày\s+)?(\d{1,2})\s*[/\-]\s*(\d{1,2})\s*[/\-]\s*(\d{4})"
    r"|(?:ngày\s+(\d{1,2})\s+tháng\s+(\d{1,2})\s+năm\s+(\d{4}))",
    re.IGNORECASE | re.UNICODE,
)

# Tiêu đề / Trích yếu: thường sau "V/v", "Về việc", "TRÍCH YẾU"
_TITLE_KEYWORDS = [
    r"v/v\s*[:.]?\s*(.+)",
    r"về\s+việc\s*[:.]?\s*(.+)",
    r"trích\s+yếu\s*[:.]?\s*(.+)",
    r"kính\s+gửi\s*[:.]?\s*(.+)",
]
_TITLE_PATTERNS = [re.compile(p, re.IGNORECASE | re.UNICODE) for p in _TITLE_KEYWORDS]

# Cơ quan ban hành: "TRƯỜNG ĐẠI HỌC KIẾN TRÚC HÀ NỘI"
_ORG_KEYWORDS = ["đại học", "trường", "phòng", "khoa", "ban", "ủy ban", "sở", "bộ"]


def extract_doc_number(lines: list[dict]) -> Optional[str]:
    """Bóc tách số hiệu công văn."""
    for line in lines:
        text = line["text"]
        m = _DOC_NUMBER_PATTERN.search(text)
        if m:
            return m.group(0).replace(" ", "")
    return None


def extract_issued_date(lines: list[dict]) -> Optional[date]:
    """Bóc tách ngày ban hành."""
    for line in lines:
        text = line["text"]
        m = _DATE_PATTERN.search(text)
        if m:
            try:
                if m.group(4):  # dạng "ngày X tháng Y năm Z"
                    day, month, year = int(m.group(4)), int(m.group(5)), int(m.group(6))
                else:           # dạng DD/MM/YYYY
                    day, month, year = int(m.group(1)), int(m.group(2)), int(m.group(3))

                if 1 <= day <= 31 and 1 <= month <= 12 and 2000 <= year <= 2100:
                    return date(year, month, day)
            except (ValueError, TypeError):
                continue
    return None


def extract_title(lines: list[dict]) -> Optional[str]:
    """Bóc tách tiêu đề / trích yếu nội dung."""
    for line in lines:
        text = line["text"]
        for pattern in _TITLE_PATTERNS:
            m = pattern.search(text)
            if m:
                title = m.group(1).strip()
                if len(title) > 10:
                    return title[:500]  # giới hạn 500 ký tự
    return None


def extract_issuing_org(lines: list[dict]) -> Optional[str]:
    """Bóc tách cơ quan ban hành (thường ở đầu trang)."""
    # Lấy 10 dòng đầu — cơ quan thường ở header
    for line in lines[:10]:
        text = line["text"].lower()
        for kw in _ORG_KEYWORDS:
            if kw in text and line["confidence"] > 0.7:
                return line["text"].strip()[:200]
    return None


def extract_fields(lines: list[dict]) -> dict:
    """
    Bóc tách toàn bộ các trường từ kết quả OCR.
    Trả về dict với các key: doc_number, issued_date, title, issuing_org.
    """
    result = {
        "doc_number": extract_doc_number(lines),
        "issued_date": extract_issued_date(lines),
        "title": extract_title(lines),
        "issuing_org": extract_issuing_org(lines),
    }
    logger.info(
        "Bóc tách: doc_number=%s | date=%s | title=%s",
        result["doc_number"],
        result["issued_date"],
        result["title"][:50] if result["title"] else None,
    )
    return result
