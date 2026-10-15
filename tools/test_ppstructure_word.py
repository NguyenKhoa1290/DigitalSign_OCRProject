"""Isolated PP-StructureV3 trial: native OCR/layout/table/Word export, no LLM."""
import argparse
import hashlib
import importlib.metadata
import json
from pathlib import Path
import time
import traceback


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--image", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--no-unwarp", action="store_true")
    parser.add_argument("--ocr-version", choices=("PP-OCRv3", "PP-OCRv5"), default="PP-OCRv5")
    parser.add_argument("--detector")
    parser.add_argument("--recognizer")
    args = parser.parse_args()
    out = args.output_dir
    out.mkdir(parents=True, exist_ok=False)
    options = {
        "device": "cpu", "cpu_threads": 4, "enable_mkldnn": False,
        "lang": "vi", "ocr_version": args.ocr_version,
        "use_doc_orientation_classify": True,
        "use_doc_unwarping": not args.no_unwarp,
        "use_textline_orientation": True,
        "use_table_recognition": True,
        "use_formula_recognition": False,
        "use_chart_recognition": False,
        "use_seal_recognition": False,
        "use_region_detection": False,
    }
    if args.detector:
        options["text_detection_model_name"] = args.detector
    if args.recognizer:
        options["text_recognition_model_name"] = args.recognizer
    metadata = {
        "input": args.image.name,
        "image_sha256": hashlib.sha256(args.image.read_bytes()).hexdigest(),
        "versions": {name: importlib.metadata.version(name) for name in
                     ("paddleocr", "paddlepaddle", "paddlex")},
        "options": options, "llm_used": False, "manual_content_edits": False,
        "status": "running",
    }
    started = time.monotonic()
    try:
        # Keep import order consistent with the working OCR environment.
        import cv2
        from paddleocr import PPStructureV3

        print("Loading PP-StructureV3", flush=True)
        pipeline = PPStructureV3(**options)
        metadata["initialization_seconds"] = round(time.monotonic() - started, 2)
        pipeline.export_paddlex_config_to_yaml(str(out / "pipeline.yaml"))
        inference_start = time.monotonic()
        results = pipeline.predict(str(args.image))
        pages = 0
        for index, result in enumerate(results, 1):
            folder = out / f"page-{index}"
            folder.mkdir()
            print(f"Export page {index}", flush=True)
            result.save_to_json(str(folder))
            result.save_to_markdown(str(folder))
            result.save_to_html(str(folder))
            result.save_to_img(str(folder))
            result.save_to_word(str(folder))
            pages += 1
        metadata.update(status="complete", pages=pages,
                        inference_and_export_seconds=round(time.monotonic() - inference_start, 2),
                        word_files=[str(p.relative_to(out)) for p in out.rglob("*.docx")])
        if not metadata["word_files"]:
            raise RuntimeError("Pipeline did not export a Word file")
    except Exception as error:
        metadata.update(status="failed", error=str(error))
        (out / "error.txt").write_text(traceback.format_exc(), encoding="utf-8")
        raise
    finally:
        metadata["total_seconds"] = round(time.monotonic() - started, 2)
        (out / "metadata.json").write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps(metadata, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    main()
