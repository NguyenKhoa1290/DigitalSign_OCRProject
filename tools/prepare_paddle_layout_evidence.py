"""Preserve existing PP-Structure evidence and count actual HTML rows, no gold data."""
import argparse
import hashlib
from html.parser import HTMLParser
import json
from pathlib import Path


class TableRows(HTMLParser):
    def __init__(self):
        super().__init__()
        self.rows = []
        self.cell = None

    def handle_starttag(self, tag, attrs):
        if tag == "tr":
            self.rows.append([])
        elif tag in ("td", "th"):
            self.cell = ""

    def handle_data(self, data):
        if self.cell is not None:
            self.cell += data

    def handle_endtag(self, tag):
        if tag in ("td", "th") and self.cell is not None:
            self.rows[-1].append(self.cell)
            self.cell = None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-dir", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    reports, variants = [], {}
    for name in ("native", "ocrv5", "ocrv6"):
        folder = args.source_dir / name
        source = next((folder / "page-1").glob("*_res.json"))
        raw = source.read_bytes()
        result = json.loads(raw)
        metadata = json.loads((folder / "metadata.json").read_text(encoding="utf-8"))
        tables = []
        for index, table in enumerate(result["table_res_list"]):
            html = table["pred_html"]
            parsed = TableRows()
            parsed.feed(html)
            report = {"table_index": index, "total_html_rows": len(parsed.rows),
                      "cell_counts_per_row": [len(row) for row in parsed.rows],
                      "parsed_rows": parsed.rows}
            tables.append({**report, "pred_html": html, "cell_box_list": table["cell_box_list"]})
        reports.append({"variant": name, "source_sha256": hashlib.sha256(raw).hexdigest(),
                        "image_sha256": metadata["image_sha256"],
                        "tables": [{k: v for k, v in t.items() if k not in ("pred_html", "cell_box_list")}
                                   for t in tables]})
        ocr = result["overall_ocr_res"]
        variants[name] = {"source_sha256": hashlib.sha256(raw).hexdigest(),
                          "image_sha256": metadata["image_sha256"],
                          "ocr_lines": [{"text": t, "confidence": s, "box": b}
                                        for t, s, b in zip(ocr["rec_texts"], ocr["rec_scores"], ocr["rec_polys"])],
                          "tables": tables}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    evidence = {"source": "existing_PP-StructureV3_output", "variant": "native",
                "manual_content_edits": False, **variants["native"]}
    args.output.write_text(json.dumps(evidence, ensure_ascii=False, indent=2), encoding="utf-8")
    args.output.with_name("paddle_table_counts.json").write_text(
        json.dumps(reports, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(reports, ensure_ascii=True))


if __name__ == "__main__":
    main()
