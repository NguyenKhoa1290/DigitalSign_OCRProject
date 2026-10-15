"""Run the layout-tool trial through Google's native generateContent API."""
import asyncio
import json
import re
import httpx
import test_paddle_llm_tools as trial


def complete_layout_schema():
    # Native Google schema validates nested array/object shapes more strictly.
    layout = trial.TOOLS[1]["function"]["parameters"]["properties"]["layout"]["properties"]
    style = {"text": {"type": "string"}, "align": {"type": "string"},
             "bold": {"type": "boolean"}, "italic": {"type": "boolean"},
             "font_size": {"type": "number"}, "space_before_pt": {"type": "number"},
             "space_after_pt": {"type": "number"}}
    paragraph = {"type": "object", "properties": {"type": {"type": "string", "enum": ["paragraph"]}, **style}, "required": ["text"]}
    block = layout["blocks"]["items"]["properties"]
    block.update(style)
    block["runs"] = {"type": "array", "items": {"type": "object", "properties": style, "required": ["text"]}}
    block["columns"] = {"type": "array", "items": {"type": "array", "items": paragraph}}
    block["rows"] = {"type": "array", "items": {"type": "array", "items": {"type": "string"}}}
    for name in ("widths", "column_widths"):
        block[name] = {"type": "array", "items": {"type": "number"}}
    for name in ("header_height_cm", "row_height_cm"):
        block[name] = {"type": "number"}
    layout["page"] = {"type": "object", "properties": {name: {"type": "number"} for name in (
        "width_cm", "height_cm", "margin_top_cm", "margin_bottom_cm", "margin_left_cm", "margin_right_cm")}}


def user_parts(content):
    if isinstance(content, str):
        return [{"text": content}]
    parts = []
    for part in content or []:
        if part["type"] == "text":
            parts.append({"text": part["text"]})
        elif part["type"] == "image_url":
            header, data = part["image_url"]["url"].split(",", 1)
            parts.append({"inlineData": {"mimeType": header[5:].split(";")[0], "data": data}})
    return parts


async def native_completion(client, payload, out, turn):
    contents, names, provider_ids = [], {}, {}
    for message in payload["messages"]:
        if message["role"] == "assistant":
            for call in message.get("tool_calls", []):
                names[call["id"]] = call["function"]["name"]
            for part in message["_google_content"].get("parts", []):
                native_call = part.get("functionCall", {})
                if native_call.get("id"):
                    provider_ids[native_call["id"]] = native_call["id"]
            contents.append(message["_google_content"])
        elif message["role"] == "tool":
            function_response = {
                "name": message.get("name") or names[message["tool_call_id"]],
                "response": json.loads(message["content"]),
            }
            if message["tool_call_id"] in provider_ids:
                function_response["id"] = provider_ids[message["tool_call_id"]]
            contents.append({"role": "user", "parts": [{"functionResponse": function_response}]})
        else:
            contents.append({"role": "user", "parts": user_parts(message["content"])})
    grouped = []
    for content in contents:
        if grouped and grouped[-1]["role"] == content["role"] == "user":
            grouped[-1]["parts"].extend(content["parts"])
        else:
            grouped.append(content)
    body = {
        "contents": grouped,
        "tools": [{"functionDeclarations": [t["function"] for t in payload["tools"]]}],
        "generationConfig": {"temperature": payload["temperature"],
            "maxOutputTokens": payload["max_tokens"],
            "thinkingConfig": {"thinkingLevel": payload["reasoning_effort"]}},
    }
    (out / "tool_schema.json").write_text(json.dumps(body["tools"], ensure_ascii=False, indent=2), encoding="utf-8")
    key = client.headers["Authorization"].removeprefix("Bearer ")
    retries = []
    async with httpx.AsyncClient(base_url="https://generativelanguage.googleapis.com/v1beta/",
                                headers={"x-goog-api-key": key}, timeout=900) as native:
        for attempt in range(4):
            response = await native.post(f"models/{payload['model']}:generateContent", json=body)
            value = response.json()
            if response.status_code < 400:
                break
            (out / f"turn-{turn}-native-error.json").write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
            match = re.search(r"retry in ([\d.]+)s", response.text, re.IGNORECASE)
            delay = float(match[1]) + 1 if response.status_code == 429 and match else 2 ** (attempt + 1)
            if attempt >= 3 or delay > 60 or response.status_code not in (429, 500, 502, 503, 504):
                raise RuntimeError(f"Native Google API HTTP {response.status_code}: {response.text[:1000]}")
            retries.append({"status": response.status_code, "wait_seconds": delay})
            (out / f"turn-{turn}-retries.json").write_text(json.dumps(retries, indent=2), encoding="utf-8")
            print(f"Native API {response.status_code}, retry in {delay:.1f}s", flush=True)
            await asyncio.sleep(delay)
    (out / f"turn-{turn}-native.json").write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding="utf-8")
    candidate = value["candidates"][0]
    content = candidate["content"]
    message = {"role": "assistant", "content": None, "_google_content": content}
    calls, texts = [], []
    for index, part in enumerate(content.get("parts", [])):
        if "functionCall" in part:
            call = part["functionCall"]
            calls.append({"id": call.get("id") or f"native-{turn}-{index}", "type": "function", "function": {
                "name": call["name"], "arguments": json.dumps(call.get("args", {}), ensure_ascii=False)}})
        elif "text" in part and not part.get("thought"):
            texts.append(part["text"])
    if calls:
        message["tool_calls"] = calls
    message["content"] = "\n".join(texts) or None
    u = value.get("usageMetadata", {})
    usage = {"prompt_tokens": u.get("promptTokenCount", 0), "completion_tokens": u.get("candidatesTokenCount", 0), "total_tokens": u.get("totalTokenCount", 0)}
    (out / f"turn-{turn}.json").write_text(json.dumps({"message": message,
        "finish_reason": candidate.get("finishReason"), "usage": usage}, ensure_ascii=False, indent=2), encoding="utf-8")
    if candidate.get("finishReason") == "MAX_TOKENS":
        raise ValueError("Native response truncated; partial calls were not executed")
    return message


if __name__ == "__main__":
    complete_layout_schema()
    trial.completion = native_completion
    asyncio.run(trial.main())
