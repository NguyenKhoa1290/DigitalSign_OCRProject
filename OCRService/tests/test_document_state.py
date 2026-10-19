"""No network requests, model calls, or application data."""
import unittest
from unittest.mock import AsyncMock, patch
import httpx
from app.services import document_service


class DocumentStateTests(unittest.IsolatedAsyncioTestCase):
    async def check(self, status, data):
        with patch.object(document_service.httpx, "AsyncClient") as client:
            get = AsyncMock(return_value=httpx.Response(status, json=data))
            client.return_value.__aenter__.return_value.get = get
            result = await document_service.require_active_document("fixture", "input.pdf", "service-fixture", True)
            self.assertEqual(get.call_args.kwargs["headers"], {"X-Service-Token": "service-fixture"})
            return result

    async def test_active_document_returns_version(self):
        result = await self.check(200, {"data": {"minioPath": "documents/input.pdf", "concurrencyVersion": "v1"}})
        self.assertEqual(result, "v1")

    async def test_deleted_stale_unauthorized_and_malformed_states_are_rejected(self):
        for status, data, expected in [
            (404, {}, 404), (401, {}, 503),
            (200, {"data": {"minioPath": "documents/replaced.pdf", "concurrencyVersion": "v1"}}, 409),
            (200, {"data": {}}, 503),
        ]:
            with self.subTest(status=status, data=data):
                with self.assertRaises(document_service.DocumentUnavailableError) as result:
                    await self.check(status, data)
                self.assertEqual(result.exception.status_code, expected)


if __name__ == "__main__":
    unittest.main()
