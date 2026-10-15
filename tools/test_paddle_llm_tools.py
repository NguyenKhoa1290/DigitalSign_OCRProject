"""Tool-calling trial: model chooses crops/layout and checks rendered Word itself."""
import argparse
import asyncio
from contextlib import asynccontextmanager
import base64
import hashlib
import io
import json
from pathlib import Path
import shutil
import subprocess
import sys
import time
import re

import httpx
from PIL import Image

project_root = Path(__file__).resolve().parents[1]
service_root = project_root / "OCRService" if (project_root / "OCRService").is_dir() else project_root
sys.path.insert(0, str(service_root))
from app.services.layout_exporter import create_layout_docx, LAYOUT_PROMPT


def image_content(image_bytes):
    return {"type": "image_url", "image_url": {"url": "data:image/jpeg;base64," + base64.b64encode(image_bytes).decode("ascii")}}


TOOLS = [
    {"type": "function", "function": {
        "name": "inspect_source", "description": "Zoom a rectangle of the original photo. Coordinates in original image pixels; returns image evidence, never corrected text.",
        "parameters": {"type": "object", "properties": {key: {"type": "integer"} for key in ("left", "top", "right", "bottom")}, "required": ["left", "top", "right", "bottom"]}}},
    {"type": "function", "function": {
        "name": "render_word", "description": "Create editable Word from YOUR complete layout and return page PNGs for visual self-review. Call again with a revised full layout to improve it.",
        "parameters": {"type": "object", "properties": {"layout": {"type": "object", "properties": {
            "page": {"type": "object"}, "font_name": {"type": "string"}, "font_size": {"type": "number"},
            "blocks": {"type": "array", "items": {"type": "object", "properties": {
                "type": {"type": "string", "enum": ["paragraph", "columns", "table"]},
                "text": {"type": "string"}, "runs": {"type": "array", "items": {"type": "object"}},
                "columns": {"type": "array", "items": {"type": "array", "items": {"type": "object"}}},
                "headers": {"type": "array", "items": {"type": "string"}},
                "rows": {"type": "array", "items": {"type": "array"}}, "blank_rows": {"type": "integer"}}, "required": ["type"]}},
            "uncertain_fields": {"type": "array", "items": {"type": "string"}}}, "required": ["blocks"]}}, "required": ["layout"]}}},
    {"type": "function", "function": {
        "name": "finish_document", "description": "Select the best successfully rendered iteration AFTER viewing its page images. Output is model-generated, with no human edits.",
        "parameters": {"type": "object", "properties": {"iteration": {"type": "integer", "minimum": 1}}, "required": ["iteration"]}}},
]


@asynccontextmanager
async def response_stream(client, payload, out, turn):
    retries = []
    for attempt in range(4):
        async with client.stream("POST", "/chat/completions", json=payload) as response:
            if response.status_code < 400:
                yield response
                return
            body = (await response.aread()).decode()
            if response.status_code >= 500 and attempt < 3:
                seconds = 2 ** (attempt + 1)
                retries.append({"attempt": attempt + 1, "status": response.status_code, "wait_seconds": seconds})
                (out / f"turn-{turn}-retries.json").write_text(json.dumps(retries, indent=2), encoding="utf-8")
                print(f"Model API {response.status_code}: retrying in {seconds}s", flush=True)
                await asyncio.sleep(seconds)
                continue
            delay = re.search(r"retry in ([\d.]+)s", body, re.IGNORECASE)
            if response.status_code == 429 and delay and attempt < 3 and float(delay[1]) <= 60:
                seconds = float(delay[1]) + 1
                retries.append({"attempt": attempt + 1, "status": 429, "wait_seconds": seconds})
                (out / f"turn-{turn}-retries.json").write_text(json.dumps(retries, indent=2), encoding="utf-8")
                print(f"Rate limit: waiting {seconds:.1f}s", flush=True)
                await asyncio.sleep(seconds)
                continue
            raise RuntimeError(f"Model API HTTP {response.status_code}: {body[:1000]}")


async def completion(client, payload, out, turn):
    calls, text, finish = {}, [], None
    usage = None
    message_extra = {}
    last_call_key = None
    stream_path = out / f"turn-{turn}-stream.jsonl"
    stream_path.write_text("", encoding="utf-8")
    last_count = 0
    async with response_stream(client, payload, out, turn) as response:
        async for line in response.aiter_lines():
            if not line.startswith("data:"):
                continue
            body = line[5:].strip()
            if body == "[DONE]":
                break
            chunk = json.loads(body)
            with stream_path.open("a", encoding="utf-8") as stream_file:
                stream_file.write(json.dumps(chunk, ensure_ascii=False) + "\n")
            if chunk.get("usage"):
                usage = chunk["usage"]
            if chunk.get("error"):
                raise RuntimeError(str(chunk["error"]))
            for choice in chunk.get("choices", []):
                finish = choice.get("finish_reason") or finish
                delta = choice.get("delta", {})
                if delta.get("extra_content"):
                    message_extra["extra_content"] = delta["extra_content"]
                text.append(delta.get("content") or "")
                for item in delta.get("tool_calls", []):
                    # Google may emit complete calls without index. Distinguish by ID.
                    index = item.get("index")
                    if index is None:
                        index = item.get("id") or last_call_key
                        if index is None:
                            raise ValueError("Tool call stream has neither index nor ID")
                    last_call_key = index
                    call = calls.setdefault(index, {"id": "", "type": "function", "function": {"name": "", "arguments": ""}})
                    if item.get("id"):
                        if call["id"] and call["id"] != item["id"]:
                            raise ValueError("Conflicting tool call IDs in stream")
                        call["id"] = item["id"]
                    fn = item.get("function", {})
                    call["function"]["name"] += fn.get("name") or ""
                    call["function"]["arguments"] += fn.get("arguments") or ""
                    # Gemini's compatibility API requires thought signatures to be echoed.
                    if item.get("extra_content"):
                        call["extra_content"] = item["extra_content"]
            count = sum(len(x["function"]["arguments"]) for x in calls.values()) + sum(map(len, text))
            if count >= last_count + 500:
                print(f"Turn {turn}: {count} characters", flush=True)
                last_count = count
                (out / f"turn-{turn}-partial.json").write_text(json.dumps({"text": "".join(text), "tool_calls": list(calls.values())}, ensure_ascii=False, indent=2), encoding="utf-8")
    message = {"role": "assistant", "content": "".join(text) or None, **message_extra}
    if calls:
        order = sorted(calls) if all(isinstance(i, int) for i in calls) else list(calls)
        message["tool_calls"] = [calls[i] for i in order]
    (out / f"turn-{turn}.json").write_text(json.dumps({"message": message, "finish_reason": finish, "usage": usage}, ensure_ascii=False, indent=2), encoding="utf-8")
    if finish == "length":
        raise ValueError("Model output truncated; no partial tool arguments executed")
    return message


async def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--image", type=Path, required=True)
    parser.add_argument("--paddle-json", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--renderer", type=Path, required=True)
    parser.add_argument("--base-url", default="http://host.docker.internal:1234/v1")
    parser.add_argument("--model", required=True)
    parser.add_argument("--prompt-file", type=Path, help="Use an explicit prompt, for example a Vietnamese trial prompt")
    parser.add_argument("--max-turns", type=int, default=6)
    parser.add_argument("--api-key-file", type=Path)
    parser.add_argument("--reasoning-effort", choices=("none", "minimal", "low", "medium", "high"), default="none")
    parser.add_argument("--resume-call", type=Path, help="Re-execute an actual saved model tool call after a renderer/interface fix; no arguments are edited")
    args = parser.parse_args()
    out = args.output_dir
    out.mkdir(parents=True, exist_ok=True)
    source = Image.open(args.image).convert("RGB")
    evidence = json.loads(args.paddle_json.read_text(encoding="utf-8"))
    # No corrected text, reviewed layout or form-specific row counts are supplied.
    if args.prompt_file:
        prompt = args.prompt_file.read_text(encoding="utf-8")
    else:
        prompt = LAYOUT_PROMPT.replace("Return valid JSON only, no markdown, matching this schema:", "Use the tools. render_word accepts a layout following this schema:")
        prompt = prompt.replace('"blank_rows":7', '"blank_rows":0')
        prompt += f"\nOriginal photo size: {source.width} x {source.height} pixels. Inspect source crops when necessary, render the document, compare render with original, then call finish_document. You must inspect the actual render before finishing. Omit default properties to keep arguments compact. Do not duplicate text in text and runs."
    if isinstance(evidence, dict):
        lines = evidence
        paddle_count = len(evidence.get("ocr_lines", []))
    else:
        lines = [{"text": x["text"], "confidence": x["confidence"], "box": x["box"]} for x in evidence]
        paddle_count = len(lines)
    messages = [{"role": "user", "content": [{"type": "text", "text": prompt + "\nBằng chứng PaddleOCR/PP-Structure hiện có:\n" + json.dumps(lines, ensure_ascii=False, separators=(",", ":"))}, image_content(args.image.read_bytes())]}]
    events, iterations, selected = [], {}, None
    started = time.monotonic()
    headers = {}
    if args.api_key_file:
        key = args.api_key_file.read_text(encoding="utf-8-sig").strip()
        if key.startswith("GEMINI_API_KEY="):
            key = key.split("=", 1)[1].strip()
        if not key:
            raise ValueError("API key file is empty")
        headers["Authorization"] = "Bearer " + key
    async with httpx.AsyncClient(base_url=args.base_url.rstrip("/") + "/", headers=headers, timeout=900) as client:
        for turn in range(1, args.max_turns + 1):
            print(f"Request turn {turn}", flush=True)
            if turn == 1 and args.resume_call:
                message = json.loads(args.resume_call.read_text(encoding="utf-8"))["message"]
                print("Replay unchanged native tool call from saved response", flush=True)
                (out / "resumed_call.json").write_text(json.dumps(message, ensure_ascii=False, indent=2), encoding="utf-8")
            else:
                payload = {"model": args.model, "messages": messages, "tools": TOOLS,
                    "tool_choice": "auto", "temperature": 0, "max_tokens": 6500, "stream": True, "reasoning_effort": args.reasoning_effort}
                if args.api_key_file:
                    payload["stream_options"] = {"include_usage": True}
                message = await completion(client, payload, out, turn)
            messages.append(message)
            calls = message.get("tool_calls", [])
            if not calls:
                events.append({"turn": turn, "error": "No native tool_calls returned"})
                messages.append({"role": "user", "content": "You have not finished the tool workflow. Call inspect_source, render_word or finish_document through tools, not JSON or tool descriptions in prose."})
                continue
            previews = []
            for call in calls:
                name = call["function"]["name"]
                try:
                    values = json.loads(call["function"]["arguments"])
                    event = {"turn": turn, "tool_call_id": call["id"], "name": name, "arguments": values}
                    print(f"Execute native tool: {name}", flush=True)
                    if name == "inspect_source":
                        left, top, right, bottom = (values[k] for k in ("left", "top", "right", "bottom"))
                        if not (0 <= left < right <= source.width and 0 <= top < bottom <= source.height):
                            raise ValueError("Crop is outside original image")
                        crop = source.crop((left, top, right, bottom))
                        crop.thumbnail((1400, 1400))
                        buffer = io.BytesIO(); crop.save(buffer, "JPEG", quality=95)
                        previews.append({"type": "text", "text": "Original source crop requested by model."})
                        previews.append(image_content(buffer.getvalue()))
                        result = {"ok": True, "source_crop": [left, top, right, bottom]}
                    elif name == "render_word":
                        index = 1 + sum(e.get("name") == "render_word" for e in events)
                        folder = out / f"iteration-{index}"
                        folder.mkdir()
                        layout = values["layout"]
                        (folder / "layout.json").write_text(json.dumps(layout, ensure_ascii=False, indent=2), encoding="utf-8")
                        word = folder / "document.docx"
                        word.write_bytes(create_layout_docx(layout))
                        process = await asyncio.create_subprocess_exec(sys.executable, str(args.renderer), str(word), "--output_dir", str(folder / "render"), "--emit_pdf", stdout=asyncio.subprocess.PIPE, stderr=asyncio.subprocess.PIPE)
                        stdout, stderr = await asyncio.wait_for(process.communicate(), 90)
                        if process.returncode:
                            raise ValueError(stderr.decode()[-2000:])
                        pages = sorted((folder / "render").glob("page-*.png"))
                        if not pages:
                            raise ValueError("Renderer returned no pages")
                        iterations[index] = word
                        previews.append({"type": "text", "text": f"Rendered iteration {index}, {len(pages)} pages. Compare with original photo and fix YOUR layout through render_word if needed; no human changes have been applied."})
                        for path in pages[:3]:
                            page = Image.open(path).convert("RGB"); page.thumbnail((1000, 1400))
                            buffer = io.BytesIO(); page.save(buffer, "JPEG", quality=90)
                            previews.append(image_content(buffer.getvalue()))
                        result = {"ok": True, "iteration": index, "pages": len(pages)}
                    elif name == "finish_document":
                        index = values["iteration"]
                        if index not in iterations:
                            raise ValueError("No such successfully rendered iteration")
                        # Rendering must have been shown to the model in an earlier API turn.
                        if not any(e.get("turn", turn) < turn and e.get("name") == "render_word" and e.get("result", {}).get("iteration") == index for e in events):
                            raise ValueError("View the render in a subsequent turn before finishing")
                        selected = index
                        shutil.copyfile(iterations[index], out / "ke_hoach_model_tool_call.docx")
                        result = {"ok": True, "selected_iteration": index}
                    else:
                        raise ValueError("Unsupported tool")
                    event["result"] = result
                except (ValueError, KeyError, TypeError, OSError, asyncio.TimeoutError) as error:
                    result = {"ok": False, "error": str(error)}
                    event = {"turn": turn, "name": name, "tool_call_id": call["id"], "result": result}
                events.append(event)
                messages.append({"role": "tool", "name": name, "tool_call_id": call["id"], "content": json.dumps(result, ensure_ascii=False)})
            (out / "tool_events.json").write_text(json.dumps(events, ensure_ascii=False, indent=2), encoding="utf-8")
            if selected:
                break
            if previews:
                messages.append({"role": "user", "content": previews})
    metadata = {"model": args.model, "api_base_url": args.base_url, "reasoning_effort": args.reasoning_effort, "image_sha256": hashlib.sha256(args.image.read_bytes()).hexdigest(),
                "paddle_lines": paddle_count, "native_tool_calls": sum("tool_call_id" in e for e in events),
                "selected_iteration": selected, "elapsed_seconds": round(time.monotonic() - started, 2), "manual_content_edits": False}
    metadata["resumed_call"] = str(args.resume_call) if args.resume_call else None
    (out / "metadata.json").write_text(json.dumps(metadata, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(metadata, ensure_ascii=False), flush=True)
    if not selected:
        raise RuntimeError("Model did not finish within turn limit; any intermediate Word files remain unedited")


if __name__ == "__main__":
    asyncio.run(main())
