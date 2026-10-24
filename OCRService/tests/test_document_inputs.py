"""Isolated inputs and mocked OCR/cloud; never uses application documents or paid APIs."""
import io
import unittest
from unittest.mock import AsyncMock, Mock, patch

from fastapi import FastAPI
from fastapi.testclient import TestClient
from PIL import Image

from app.api import routes
from app.services import document_input, ocr_processor


def pdf(pages):
    images = [Image.new("RGB", (24, 32), (number * 30, 255, 255)) for number in range(pages)]
    output = io.BytesIO()
    images[0].save(output, "PDF", save_all=True, append_images=images[1:])
    return output.getvalue()


def picture(format="PNG"):
    output = io.BytesIO()
    Image.new("RGB", (24, 32), "white").save(output, format)
    return output.getvalue()


class DocumentInputsTests(unittest.TestCase):
    def test_selected_pages_validate_deduplicate_and_sort_before_rendering(self):
        self.assertEqual(document_input.select_pages("8, 1-3, 3", 89), [1, 2, 3, 8])
        for expression in ["1-6", "0", "90", "3-1", "", "1,,2", "x", "1,2,3,4,5,8", "1-999999999"]:
            with self.subTest(expression=expression):
                with self.assertRaises(document_input.DocumentInputError):
                    document_input.select_pages(expression, 89)

    def test_long_pdf_renders_only_selected_original_page_numbers(self):
        with patch.object(document_input, "convert_from_bytes", return_value=[Image.new("RGB", (24, 32))]) as render:
            images = document_input.load_document_images(pdf(8), "eight.pdf", pages="8, 2-3")
            self.assertEqual(len(images), 3)
            self.assertEqual([(call.kwargs["first_page"], call.kwargs["last_page"]) for call in render.call_args_list], [(2, 2), (3, 3), (8, 8)])

    def test_input_info_counts_long_pdf_without_rendering_or_cloud(self):
        with patch.object(document_input, "convert_from_bytes") as render, \
             patch.object(routes.gemini_service, "get_gemini_api_key", new_callable=AsyncMock) as key:
            response = self.client.post("/api/ocr/input-info", files={"file": ("long.pdf", pdf(8), "application/pdf")})
            self.assertEqual(response.status_code, 200, response.text)
            self.assertEqual(response.json()["totalPages"], 8)
            render.assert_not_called(); key.assert_not_awaited()

    def test_multiple_inputs_reject_mixed_pdf_or_more_than_five_selected_before_cloud(self):
        with patch.object(routes.gemini_service, "get_gemini_api_key", new_callable=AsyncMock) as key:
            url = "/api/ocr/gemini/export-word-upload?model_id=gemini-fixture"
            response = self.client.post(url + "&pages=1", files=[("files", ("a.pdf", pdf(1), "application/pdf")), ("files", ("b.png", picture(), "image/png"))])
            self.assertEqual(response.status_code, 400, response.text)
            response = self.client.post(url + "&pages=1-6", files=[("files", (f"{i}.png", picture(), "image/png")) for i in range(6)])
            self.assertEqual(response.status_code, 400, response.text)
            key.assert_not_awaited()

    def test_multi_image_selection_and_long_pdf_call_only_selected_pages_and_chosen_model(self):
        engine = Mock(); engine.extract_lines.return_value = []
        with patch.object(ocr_processor, "get_engine", return_value=engine), \
             patch.object(routes.gemini_service, "get_gemini_api_key", new_callable=AsyncMock, return_value="fixture-only"), \
             patch.object(routes.gemini_service, "list_gemini_models", new_callable=AsyncMock, return_value=[{"id": "gemini-fixture"}]), \
             patch.object(routes.gemini_service, "generate_layout", new_callable=AsyncMock, return_value={"blocks": []}) as generate, \
             patch.object(routes, "create_layout_docx", return_value=b"fixture-docx"):
            response = self.client.post("/api/ocr/gemini/export-word-upload?model_id=gemini-fixture&pages=8,2", files=[("files", ("long.pdf", pdf(8), "application/pdf"))])
            self.assertEqual(response.status_code, 200, response.text)
            self.assertEqual(response.headers["X-OCR-Pages"], "2,8")
            self.assertEqual(generate.await_count, 2)
            self.assertEqual(generate.call_args.args[2], "gemini-fixture")
            generate.reset_mock(); engine.reset_mock()
            response = self.client.post("/api/ocr/gemini/export-word-upload?model_id=gemini-fixture&pages=2,6", files=[("files", (f"{i}.png", picture(), "image/png")) for i in range(6)])
            self.assertEqual(response.status_code, 200, response.text)
            self.assertEqual(response.headers["X-OCR-Pages"], "2,6")
            self.assertEqual(generate.await_count, 2)
            self.assertEqual(engine.extract_lines.call_count, 2)

    def setUp(self):
        app = FastAPI()
        app.include_router(routes.router)
        self.client = TestClient(app)

    def test_pdf_count_is_checked_before_rendering_over_limit(self):
        with patch.object(document_input, "convert_from_bytes") as render:
            with self.assertRaisesRegex(document_input.DocumentInputError, "6 trang"):
                document_input.load_document_images(pdf(6), "input.pdf")
            render.assert_not_called()

    def test_pdf_exactly_five_pages_decodes_all_pages(self):
        self.assertEqual(len(document_input.load_document_images(pdf(5), "input.PDF")), 5)

    def test_supported_images_decode_as_one_page(self):
        for name, format in [("input.png", "PNG"), ("input.jpg", "JPEG"), ("input.jpeg", "JPEG"), ("input.webp", "WEBP")]:
            with self.subTest(name=name):
                pages = document_input.load_document_images(picture(format), name)
                self.assertEqual(len(pages), 1)
                self.assertEqual(pages[0].mode, "RGB")

    def test_phone_image_exif_orientation_is_applied(self):
        output = io.BytesIO()
        image = Image.new("RGB", (16, 24), "white")
        exif = image.getexif(); exif[274] = 6
        image.save(output, "JPEG", exif=exif)
        self.assertEqual(document_input.load_document_images(output.getvalue(), "phone.jpg")[0].size, (24, 16))

    def test_animated_image_is_not_silently_truncated(self):
        output = io.BytesIO()
        Image.new("RGB", (24, 32), "white").save(output, "WEBP", save_all=True,
            append_images=[Image.new("RGB", (24, 32), "black")], duration=100, loop=0)
        with self.assertRaisesRegex(document_input.DocumentInputError, "ảnh tĩnh"):
            document_input.load_document_images(output.getvalue(), "animated.webp")

    def test_over_limit_rejected_on_every_upload_route_without_ocr_or_google(self):
        with patch.object(ocr_processor, "get_engine") as engine, \
             patch.object(routes.gemini_service, "get_gemini_api_key", new_callable=AsyncMock) as key, \
             patch.object(routes.gemini_service, "generate_layout", new_callable=AsyncMock) as generate:
            for route in ["/process-upload", "/export-word-upload", "/gemini/export-word-upload?model_id=gemini-fixture"]:
                with self.subTest(route=route):
                    response = self.client.post("/api/ocr" + route, files={"file": ("input.pdf", pdf(6), "application/pdf")})
                    self.assertEqual(response.status_code, 400, response.text)
                    self.assertIn("5 trang", response.json()["detail"])
            engine.assert_not_called(); key.assert_not_awaited(); generate.assert_not_awaited()

    def test_stored_document_over_limit_does_not_ocr_or_update_database(self):
        with patch.object(ocr_processor.minio_service, "download_file", return_value=pdf(6)), \
             patch.object(ocr_processor.document_service, "require_active_document", new_callable=AsyncMock, return_value="fixture-version"), \
             patch.object(ocr_processor, "get_engine") as engine, \
             patch.object(ocr_processor.document_service, "update_ocr_result", new_callable=AsyncMock) as update:
            response = self.client.post("/api/ocr/process", json={"doc_id": "fixture", "minio_path": "documents/input.pdf"})
            self.assertEqual(response.status_code, 400, response.text)
            engine.assert_not_called(); update.assert_not_awaited()

    def test_deleted_document_is_rejected_before_minio_or_ocr(self):
        with patch.object(ocr_processor.document_service, "require_active_document", new_callable=AsyncMock,
                          side_effect=ocr_processor.document_service.DocumentUnavailableError("Đã xóa", 404)), \
             patch.object(ocr_processor.minio_service, "download_file") as download, \
             patch.object(ocr_processor, "get_engine") as engine:
            response = self.client.post("/api/ocr/process", json={"doc_id": "fixture", "minio_path": "input.pdf"})
            self.assertEqual(response.status_code, 404, response.text)
            download.assert_not_called(); engine.assert_not_called()

    def test_callback_failure_returns_conflict_and_includes_original_version(self):
        engine = Mock(); engine.extract_lines.return_value = []
        with patch.object(ocr_processor.document_service, "require_active_document", new_callable=AsyncMock, return_value="original-version"), \
             patch.object(ocr_processor.minio_service, "download_file", return_value=picture()), \
             patch.object(ocr_processor, "get_engine", return_value=engine), \
             patch.object(ocr_processor.document_service, "update_ocr_result", new_callable=AsyncMock, return_value=False) as update, \
             patch.object(ocr_processor, "get_settings", return_value=Mock(service_token="fixture")):
            response = self.client.post("/api/ocr/process", json={"doc_id": "fixture", "minio_path": "input.png"})
            self.assertEqual(response.status_code, 409, response.text)
            self.assertEqual(update.call_args.args[1]["expectedVersion"], "original-version")

    def test_paddle_upload_recognizes_exactly_five_pages_or_one_image(self):
        engine = Mock(); engine.extract_lines.return_value = []
        with patch.object(ocr_processor, "get_engine", return_value=engine):
            response = self.client.post("/api/ocr/process-upload", files={"file": ("five.pdf", pdf(5), "application/pdf")})
            self.assertEqual(response.status_code, 200, response.text)
            self.assertEqual(response.json()["total_pages"], 5)
            self.assertEqual(engine.extract_lines.call_count, 5)
            engine.reset_mock()
            response = self.client.post("/api/ocr/process-upload", files={"file": ("photo.png", picture(), "image/png")})
            self.assertEqual(response.status_code, 200, response.text)
            self.assertEqual(response.json()["total_pages"], 1)
            engine.extract_lines.assert_called_once()

    def test_gemini_exactly_five_pages_calls_layout_per_page(self):
        engine = Mock(); engine.extract_lines.return_value = []
        with patch.object(ocr_processor, "get_engine", return_value=engine), \
             patch.object(routes.gemini_service, "get_gemini_api_key", new_callable=AsyncMock, return_value="fixture-only"), \
             patch.object(routes.gemini_service, "list_gemini_models", new_callable=AsyncMock, return_value=[{"id": "gemini-fixture"}]), \
             patch.object(routes.gemini_service, "generate_layout", new_callable=AsyncMock, return_value={"blocks": []}) as generate, \
             patch.object(routes, "create_layout_docx", return_value=b"fixture-docx") as export:
            response = self.client.post("/api/ocr/gemini/export-word-upload?model_id=gemini-fixture",
                files={"file": ("five.pdf", pdf(5), "application/pdf")})
            self.assertEqual(response.status_code, 200, response.text)
            self.assertEqual(generate.await_count, 5)
            self.assertEqual(engine.extract_lines.call_count, 5)
            self.assertEqual(len(export.call_args.args[0]["blocks"]), 4)  # Four page breaks.

    def test_corrupt_or_unsupported_input_returns_400_before_cloud(self):
        with patch.object(routes.gemini_service, "get_gemini_api_key", new_callable=AsyncMock) as key:
            for name in ["corrupt.png", "corrupt.pdf", "input.svg"]:
                response = self.client.post("/api/ocr/gemini/export-word-upload?model_id=gemini-fixture",
                    files={"file": (name, b"not an image", "image/png")})
                self.assertEqual(response.status_code, 400, response.text)
            key.assert_not_awaited()


if __name__ == "__main__":
    unittest.main()
