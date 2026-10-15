"""Local trial. Run inside OCR Docker; saves raw evidence and editable Word."""
import argparse
import asyncio
import hashlib
import json
from pathlib import Path
import sys
import time

# Load OpenCV before Paddle to avoid the native-library import crash observed locally.
import cv2

project_root = Path(__file__).resolve().parents[1]
service_root = project_root / "OCRService" if (project_root / "OCRService").is_dir() else project_root
sys.path.insert(0, str(service_root))
from app.ocr.engine import OcrEngine
from app.services.layout_exporter import extract_layout, create_layout_docx


async def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--image", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--paddle-json", type=Path)
    parser.add_argument("--base-url", default="http://host.docker.internal:1234/v1")
    parser.add_argument("--model", required=True)
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    started = time.monotonic()
    lines = json.loads(args.paddle_json.read_text(encoding="utf-8")) if args.paddle_json else OcrEngine().extract_lines(str(args.image))
    image_bytes = args.image.read_bytes()
    def progress(content):
        (args.output_dir / "llm_partial.txt").write_text(content, encoding="utf-8")
        print(f"LLM output: {len(content)} characters", flush=True)
    layout, response = await extract_layout(image_bytes, lines, base_url=args.base_url, model=args.model, progress=progress)
    for filename, value in (("paddle_lines.json", lines), ("layout.json", layout), ("llm_response.json", response)):
        (args.output_dir / filename).write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
    (args.output_dir / "ke_hoach_paddle_llm.docx").write_bytes(create_layout_docx(layout))
    metadata = {"image_sha256": hashlib.sha256(image_bytes).hexdigest(), "model": args.model,
                "paddle_lines": len(lines), "elapsed_seconds": round(time.monotonic() - started, 2),
                "uncertain_fields": layout.get("uncertain_fields", [])}
    (args.output_dir / "metadata.json").write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(metadata, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    asyncio.run(main())
