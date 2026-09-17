from pydantic_settings import BaseSettings
from functools import lru_cache


class Settings(BaseSettings):
    # App
    app_name: str = "OCR Service"
    app_version: str = "1.0.0"
    debug: bool = False
    host: str = "0.0.0.0"
    port: int = 5051

    # MinIO
    minio_endpoint: str = "localhost:9000"
    minio_access_key: str = "minioadmin"
    minio_secret_key: str = "minioadmin"
    minio_use_ssl: bool = False
    minio_bucket: str = "documents"

    # Document Service
    document_service_url: str = "http://localhost:5049"
    service_token: str = ""  # Service-token nội bộ dùng khi Kafka event không có JWT

    # Kafka (optional)
    kafka_enabled: bool = False
    kafka_bootstrap_servers: str = "localhost:9092"
    kafka_topic_document_uploaded: str = "document.uploaded"
    kafka_consumer_group: str = "ocr-service-group"

    # OCR
    ocr_language: str = "vi"  # Tiếng Việt
    ocr_use_gpu: bool = False  # Set True nếu có GPU

    class Config:
        env_file = ".env"
        env_file_encoding = "utf-8"


@lru_cache
def get_settings() -> Settings:
    return Settings()
