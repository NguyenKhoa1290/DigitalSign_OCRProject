"""Attached-file flow: mocked HTTP/MinIO/cloud, actual PDF selection; never application data."""
import io
import unittest
from contextlib import ExitStack
from unittest.mock import AsyncMock, Mock, patch

import httpx
from fastapi import FastAPI
from fastapi.testclient import TestClient
from PIL import Image
from minio.error import S3Error

from app.api import routes
from app.services import document_service, minio_service

DOC = "bbbbbbbb-0000-0000-0000-000000000001"
VERSION = "cccccccc-0000-0000-0000-000000000001"


def pdf():
    images = [Image.new("RGB", (24, 32), "white") for _ in range(8)]
    output = io.BytesIO()
    images[0].save(output, "PDF", save_all=True, append_images=images[1:])
    return output.getvalue()


class AttachedOcrTests(unittest.TestCase):
    def test_internal_word_export_uses_saved_layout_without_running_ai(self):
        app = FastAPI(); app.include_router(routes.router)
        with TestClient(app) as client, patch.object(routes, "get_settings", return_value=Mock(service_token="fixture-service")), \
             patch.object(routes.gemini_service, "generate_layout", new_callable=AsyncMock) as generate, \
             patch.object(routes.ocr_processor, "get_engine") as engine:
            payload={"layout":{"blocks":[{"type":"paragraph","text":"Nội dung đã lưu"}]}}
            self.assertEqual(client.post("/api/ocr/internal/export-layout",json=payload).status_code,401)
            response=client.post("/api/ocr/internal/export-layout",headers={"X-Service-Token":"fixture-service"},json=payload)
            self.assertEqual(response.status_code,200,response.text)
            self.assertTrue(response.content.startswith(b"PK"))
            generate.assert_not_awaited(); engine.assert_not_called()

    def setUp(self):
        app = FastAPI(); app.include_router(routes.router)
        self.client = TestClient(app)
        self.stack = ExitStack(); self.addCleanup(self.stack.close)
        http = self.stack.enter_context(patch.object(document_service.httpx, "AsyncClient"))
        self.get = AsyncMock(return_value=httpx.Response(200, json={"data": {"minioPath": "documents/actual.pdf", "concurrencyVersion": VERSION}}))
        http.return_value.__aenter__.return_value.get = self.get
        self.download = self.stack.enter_context(patch.object(minio_service, "download_file", return_value=pdf()))
        engine = Mock(); engine.extract_lines.return_value = []
        self.engine = self.stack.enter_context(patch.object(routes.ocr_processor, "get_engine", return_value=engine))
        self.key = self.stack.enter_context(patch.object(routes.gemini_service, "get_gemini_api_key", new_callable=AsyncMock, return_value="fixture"))
        self.stack.enter_context(patch.object(routes.gemini_service, "list_gemini_models", new_callable=AsyncMock, return_value=[{"id": "gemini-fixture"}]))
        self.generate = self.stack.enter_context(patch.object(routes.gemini_service, "generate_layout", new_callable=AsyncMock, return_value={"blocks": [{"type": "paragraph", "text": "Result"}]}))

    def request(self, action, **data):
        return self.client.post(f"/api/ocr/gemini/documents/{DOC}/{action}",
            headers={"Authorization": "Bearer fixture"}, json={"expectedVersion": VERSION, **data})

    def test_page_count_uses_server_attachment_and_forwards_user_authorization_without_ai(self):
        response = self.request("input-info", minioPath="other/private.pdf")
        self.assertEqual(response.status_code, 200, response.text)
        self.assertEqual(response.json()["totalPages"], 8)
        self.download.assert_called_once_with("actual.pdf", max_bytes=25 * 1024 * 1024)
        self.assertEqual(self.get.call_args.kwargs["headers"], {"Authorization": "Bearer fixture"})
        self.engine.assert_not_called(); self.key.assert_not_awaited()

    def test_recognition_reads_saved_file_and_only_selected_pages_with_chosen_model(self):
        response = self.request("recognize", modelId="gemini-fixture", pages="8,2", minioPath="ignored.pdf")
        self.assertEqual(response.status_code, 200, response.text)
        self.assertEqual(response.json()["selectedPages"], [2, 8])
        self.download.assert_called_once_with("actual.pdf", max_bytes=25 * 1024 * 1024)
        self.assertEqual(self.generate.await_count, 2)
        self.assertEqual(self.generate.call_args.args[2], "gemini-fixture")

    def test_missing_deleted_stale_and_word_are_rejected_before_storage_or_ai(self):
        cases = [(404, {}, 404), (401, {}, 401), (403, {}, 403),
            (200, {"data": {"minioPath": "", "concurrencyVersion": VERSION}}, 400),
            (200, {"data": {"minioPath": "actual.pdf", "concurrencyVersion": "cccccccc-0000-0000-0000-000000000002"}}, 409),
            (200, {"data": {"minioPath": "actual.docx", "concurrencyVersion": VERSION}}, 400)]
        for status, body, expected in cases:
            self.get.return_value = httpx.Response(status, json=body)
            response = self.request("recognize", modelId="gemini-fixture", pages="1")
            self.assertEqual(response.status_code, expected, response.text)
        self.download.assert_not_called(); self.key.assert_not_awaited(); self.generate.assert_not_awaited()

    def test_no_bearer_or_more_than_five_pages_never_calls_ai(self):
        response = self.client.post(f"/api/ocr/gemini/documents/{DOC}/input-info", json={"expectedVersion": VERSION})
        self.assertEqual(response.status_code, 401); self.get.assert_not_awaited()
        response = self.request("recognize", modelId="gemini-fixture", pages="1-6")
        self.assertEqual(response.status_code, 400); self.key.assert_not_awaited(); self.engine.assert_not_called()

    def test_missing_storage_object_and_oversize_file_are_clear_errors_without_ai(self):
        self.download.side_effect = S3Error("NoSuchKey", "Missing", "fixture", "fixture", "fixture", None)
        response = self.request("recognize", modelId="gemini-fixture", pages="1")
        self.assertEqual(response.status_code, 404)
        self.download.side_effect = ValueError("Tệp vượt quá giới hạn 25 MB")
        response = self.request("input-info")
        self.assertEqual(response.status_code, 413)
        self.key.assert_not_awaited()

    def internal_request(self, token="fixture-service", **data):
        return self.client.post("/api/ocr/internal/gemini-job", headers={"X-Service-Token": token},
            json={"docId": DOC, "sourcePath": "documents/actual.pdf", "modelId": "gemini-fixture", "pages": "8,2", **data})

    def test_background_job_requires_service_auth_before_storage_or_ai(self):
        with patch.object(routes, "get_settings", return_value=Mock(service_token="fixture-service")):
            for token in ("", "incorrect"):
                response = self.internal_request(token)
                self.assertEqual(response.status_code, 401, response.text)
        self.get.assert_not_awaited(); self.download.assert_not_called(); self.generate.assert_not_awaited()

    def test_background_job_forwards_service_auth_and_uses_saved_source_pages_and_model(self):
        with patch.object(routes, "get_settings", return_value=Mock(service_token="fixture-service", minio_bucket="documents")):
            response = self.internal_request()
        self.assertEqual(response.status_code, 200, response.text)
        self.assertEqual(response.json()["selectedPages"], [2, 8])
        self.assertEqual(self.get.call_args.kwargs["headers"], {"X-Service-Token": "fixture-service"})
        self.download.assert_called_once_with("actual.pdf", max_bytes=25 * 1024 * 1024)
        self.assertEqual(self.generate.await_count, 2)
        self.assertEqual(self.generate.call_args.args[2], "gemini-fixture")

    def test_background_job_rejects_replaced_deleted_and_over_limit_sources_before_ai(self):
        with patch.object(routes, "get_settings", return_value=Mock(service_token="fixture-service", minio_bucket="documents")):
            response = self.internal_request(sourcePath="documents/replaced.pdf")
            self.assertEqual(response.status_code, 409, response.text)
            self.get.return_value = httpx.Response(404, json={})
            self.assertEqual(self.internal_request().status_code, 404)
            self.get.return_value = httpx.Response(200, json={"data": {"minioPath": "documents/actual.pdf", "concurrencyVersion": VERSION}})
            self.assertEqual(self.internal_request(pages="1-6").status_code, 400)
        self.key.assert_not_awaited(); self.generate.assert_not_awaited()


class LimitedDownloadTests(unittest.TestCase):
    def test_oversize_download_is_bounded_and_response_is_always_released(self):
        with patch.object(minio_service, "_get_client") as client:
            response = Mock(); response.read.return_value = b"123"
            client.return_value.get_object.return_value = response
            with self.assertRaises(ValueError): minio_service.download_file("fixture.pdf", max_bytes=2)
            response.read.assert_called_once_with(3)
            response.close.assert_called_once(); response.release_conn.assert_called_once()
