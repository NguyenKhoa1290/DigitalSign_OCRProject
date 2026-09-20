import logging
import asyncio
from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from app.api.routes import router
from app.config import get_settings
from app.services import kafka_consumer

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s | %(levelname)-8s | %(name)s | %(message)s",
    datefmt="%Y-%m-%d %H:%M:%S",
)
logger = logging.getLogger(__name__)


@asynccontextmanager
async def lifespan(app: FastAPI):
    """Khởi động và dọn dẹp resources."""
    settings = get_settings()
    loop = asyncio.get_running_loop()
    logger.info("OCR Service đang khởi động (port=%d)...", settings.port)

    # Khởi động Kafka consumer nếu được bật
    def _sync_process(doc_id, minio_path, token):
        """Kafka cần sync wrapper vì consumer chạy trong thread."""
        future = asyncio.run_coroutine_threadsafe(
            __import__("app.services.ocr_processor", fromlist=["process_document"])
            .process_document(doc_id, minio_path, token),
            loop,
        )
        future.result()

    kafka_consumer.start_consumer(_sync_process)
    logger.info("OCR Service sẵn sàng.")

    yield

    kafka_consumer.stop_consumer()
    logger.info("OCR Service đã dừng.")


settings = get_settings()

app = FastAPI(
    title="OCR Service",
    description="Microservice nhận diện ký tự quang học (PaddleOCR) cho hệ thống quản lý công văn HAU",
    version=settings.app_version,
    lifespan=lifespan,
)

# CORS
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

# Routes
app.include_router(router)


@app.get("/", tags=["Root"])
async def root():
    return {
        "service": settings.app_name,
        "version": settings.app_version,
        "docs": "/docs",
        "health": "/api/ocr/health",
    }
