import io
import logging
import tempfile
import os
from minio import Minio
from minio.error import S3Error
from app.config import get_settings

logger = logging.getLogger(__name__)


def _get_client() -> Minio:
    s = get_settings()
    return Minio(
        s.minio_endpoint,
        access_key=s.minio_access_key,
        secret_key=s.minio_secret_key,
        secure=s.minio_use_ssl,
    )


def download_file(object_name: str) -> bytes:
    """Tải file từ MinIO, trả về bytes."""
    client = _get_client()
    settings = get_settings()
    try:
        response = client.get_object(settings.minio_bucket, object_name)
        data = response.read()
        response.close()
        response.release_conn()
        logger.info("Tải file từ MinIO: %s (%d bytes)", object_name, len(data))
        return data
    except S3Error as e:
        logger.error("Lỗi tải file MinIO %s: %s", object_name, e)
        raise


def upload_file(object_name: str, data: bytes, content_type: str = "application/pdf") -> None:
    """Upload file lên MinIO."""
    client = _get_client()
    settings = get_settings()
    try:
        client.put_object(
            settings.minio_bucket,
            object_name,
            io.BytesIO(data),
            length=len(data),
            content_type=content_type,
        )
        logger.info("Upload file lên MinIO: %s (%d bytes)", object_name, len(data))
    except S3Error as e:
        logger.error("Lỗi upload MinIO %s: %s", object_name, e)
        raise
