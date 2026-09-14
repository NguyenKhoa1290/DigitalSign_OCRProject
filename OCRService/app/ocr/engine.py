import logging
import re
from typing import Optional
from paddleocr import PaddleOCR

logger = logging.getLogger(__name__)


class OcrEngine:
    """Wrapper cho PaddleOCR với tiếng Việt."""

    def __init__(self, language: str = "vi", use_gpu: bool = False):
        logger.info("Khởi tạo PaddleOCR engine (lang=%s, gpu=%s)...", language, use_gpu)
        self._ocr = PaddleOCR(
            use_angle_cls=True,
            lang=language,
            use_gpu=use_gpu,
            show_log=False,
        )
        logger.info("PaddleOCR engine đã sẵn sàng.")

    def extract_raw(self, image_path: str) -> list:
        """
        Chạy OCR trên ảnh, trả về kết quả thô từ PaddleOCR.
        Format: [[[box, (text, confidence)], ...], ...]
        """
        result = self._ocr.ocr(image_path, cls=True)
        return result or []

    def extract_lines(self, image_path: str) -> list[dict]:
        """
        Trả về danh sách các dòng text với confidence.
        [{"text": "...", "confidence": 0.99, "box": [[x1,y1],[x2,y2],[x3,y3],[x4,y4]]}]
        """
        raw = self.extract_raw(image_path)
        lines = []
        for page in raw:
            if not page:
                continue
            for item in page:
                box, (text, confidence) = item
                lines.append({
                    "text": text.strip(),
                    "confidence": round(float(confidence), 4),
                    "box": box,
                })
        return lines
