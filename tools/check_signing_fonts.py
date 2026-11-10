"""Check the fonts LibreOffice selects for Vietnamese in the signing container."""
import argparse
import json
import subprocess


def docker_text(container, *command):
    return subprocess.check_output(
        ["docker", "exec", container, *command], encoding="utf-8"
    ).strip()


def check_fonts(container):
    required = set(range(0x1EA0, 0x1EFA)) | set(map(ord, "ĂăÂâÊêÔôƠơƯưĐđ"))
    results = []
    for style in ("Regular", "Bold", "Italic", "Bold Italic"):
        path = docker_text(container, "fc-match", "-f", "%{file}",
                           "Times New Roman:style=" + style)
        charset = docker_text(container, "fc-query", "--format", "%{charset}", path)
        supported = set()
        for span in charset.split():
            bounds = span.split("-")
            start, end = int(bounds[0], 16), int(bounds[-1], 16)
            supported.update(range(start, end + 1))
        missing = sorted(required - supported)
        if missing:
            raise AssertionError(f"{style}: {path} lacks Vietnamese glyphs: "
                                 + ", ".join(f"U+{code:04X}" for code in missing))
        results.append({"style": style, "font": path,
                        "vietnamese_glyphs_checked": len(required)})
    return results


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--container", default="hau_sign_service")
    args = parser.parse_args()
    print(json.dumps(check_fonts(args.container), ensure_ascii=False, indent=2))
