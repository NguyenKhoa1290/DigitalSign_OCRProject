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
             patch.object(ocr_processor, "get_engine") as engine, \
             patch.object(ocr_processor.document_service, "update_ocr_result", new_callable=AsyncMock) as update:
            response = self.client.post("/api/ocr/process", json={"doc_id": "fixture", "minio_path": "documents/input.pdf"})
            self.assertEqual(response.status_code, 400, response.text)
            engine.assert_not_called(); update.assert_not_awaited()

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
