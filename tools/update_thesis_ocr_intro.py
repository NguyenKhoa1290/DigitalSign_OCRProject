"""Update section 1.2.3 and its cached TOC label; preserve all other content."""
import argparse
import hashlib
import json
from pathlib import Path
from zipfile import ZipFile

from lxml import etree

NS = {"w": "http://schemas.openxmlformats.org/wordprocessingml/2006/main"}
TITLE_OLD = "1.2.3. Công nghệ nhận dạng ký tự quang học PaddleOCR"
TITLE_NEW = "1.2.3. PaddleOCR và mô hình ngôn ngữ lớn"
PARAGRAPHS = [
    "PaddleOCR được sử dụng để nhận diện chữ trong ảnh tài liệu. Trong phương án tái tạo Word, PP-StructureV3 bổ sung phân tích bố cục, cấu trúc bảng và tọa độ ô, cung cấp bằng chứng nhận diện cho bước xử lý tiếp theo.",
    "Mô hình ngôn ngữ lớn đa phương thức được lựa chọn là Gemini 3.8 Flash. Model đọc ảnh gốc cùng văn bản và cấu trúc bảng Paddle, đối chiếu nội dung tiếng Việt và tạo JSON bố cục theo schema. Bộ xuất local chuyển JSON thành đoạn văn và bảng Word có thể chỉnh sửa.",
    "Phương án kết hợp hỗ trợ văn thư giảm thao tác nhập liệu và khôi phục tài liệu. Word vẫn là bản nháp cần đối chiếu, sửa lỗi và xác nhận. Kết quả thử nghiệm, hạn chế và phạm vi tích hợp còn lại được trình bày tại mục 2.7; không mặc định xem nội dung tự động là chính xác hoàn toàn.",
]


def text(p):
    return "".join(p.xpath(".//w:t/text()", namespaces=NS))


def replace_text(p, value):
    nodes = p.findall(".//w:t", NS)
    nodes[0].text = value
    for node in nodes[1:]:
        node.text = ""


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.parent.mkdir(parents=True, exist_ok=True)
    if args.output.exists():
        raise ValueError("Output exists")
    with ZipFile(args.source) as source, ZipFile(args.output, "w") as target:
        original = source.read("word/document.xml")
        root = etree.fromstring(original)
        paragraphs = root.findall("w:body/w:p", NS)
        matches = [i for i,p in enumerate(paragraphs) if text(p) == TITLE_OLD]
        if len(matches) != 1:
            raise ValueError("Expected one body section 1.2.3")
        index = matches[0]
        if not text(paragraphs[index+4]).startswith("1.2.4."):
            raise ValueError("Unexpected section boundaries")
        changed = [index, index+1, index+2, index+3]
        replace_text(paragraphs[index], TITLE_NEW)
        for p, value in zip(paragraphs[index+1:index+4], PARAGRAPHS):
            replace_text(p, value)
        toc_labels = 0
        for i,p in enumerate(paragraphs[:index]):
            for node in p.findall(".//w:t", NS):
                if TITLE_OLD in (node.text or ""):
                    node.text = node.text.replace(TITLE_OLD, TITLE_NEW)
                    changed.append(i)
                    toc_labels += 1
        if toc_labels != 1:
            raise ValueError("Expected one cached TOC label")
        before = etree.fromstring(original).findall("w:body/w:p", NS)
        for i,(a,b) in enumerate(zip(before, paragraphs)):
            if i not in changed and etree.tostring(a) != etree.tostring(b):
                raise AssertionError(f"Unexpected paragraph change {i}")
        updated = etree.tostring(root, xml_declaration=True, encoding="UTF-8", standalone=True)
        for entry in source.infolist():
            target.writestr(entry, updated if entry.filename == "word/document.xml" else source.read(entry.filename))
    with ZipFile(args.source) as source, ZipFile(args.output) as target:
        other_parts = all(source.read(name) == target.read(name) for name in source.namelist()
                          if name != "word/document.xml")
    metadata = {"source_sha256": hashlib.sha256(args.source.read_bytes()).hexdigest(),
                "output_sha256": hashlib.sha256(args.output.read_bytes()).hexdigest(),
                "modified_section": "1.2.3", "modified_paragraph_indices": changed,
                "cached_toc_label_updated": True, "other_paragraphs_preserved": True,
                "other_package_parts_preserved": other_parts, "model_calls": 0}
    args.output.with_suffix(".verification.json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")
    print(json.dumps(metadata))


if __name__ == "__main__":
    main()
