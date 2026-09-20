import json
import logging
import threading
import time
from typing import Optional
from app.config import get_settings

logger = logging.getLogger(__name__)

_consumer_thread: Optional[threading.Thread] = None
_running = False


def start_consumer(process_fn):
    """
    Khởi động Kafka consumer trong background thread.
    process_fn(doc_id: str, minio_path: str, token: str) -> None
    """
    global _consumer_thread, _running
    settings = get_settings()

    if not settings.kafka_enabled:
        logger.info("Kafka bị tắt (KAFKA_ENABLED=false). Bỏ qua consumer.")
        return

    try:
        from kafka import KafkaConsumer
    except ImportError:
        logger.warning("kafka-python chưa được cài. Kafka consumer không khởi động.")
        return

    _running = True

    def _run():
        logger.info(
            "Kafka consumer bắt đầu: servers=%s | topic=%s | group=%s",
            settings.kafka_bootstrap_servers,
            settings.kafka_topic_document_uploaded,
            settings.kafka_consumer_group,
        )
        while _running:
            consumer = None
            try:
                consumer = KafkaConsumer(
                    settings.kafka_topic_document_uploaded,
                    bootstrap_servers=settings.kafka_bootstrap_servers,
                    group_id=settings.kafka_consumer_group,
                    auto_offset_reset="earliest",
                    value_deserializer=lambda v: json.loads(v.decode("utf-8")),
                )
                logger.info("Kafka consumer đã kết nối thành công.")

                for message in consumer:
                    if not _running:
                        break
                    try:
                        payload = message.value
                        # Payload expected: { "doc_id": "...", "minio_path": "...", "token": "..." }
                        doc_id = payload.get("doc_id", "")
                        minio_path = payload.get("minio_path", "")
                        token = payload.get("token", "")
                        logger.info("Nhận Kafka event: doc_id=%s", doc_id)
                        process_fn(doc_id, minio_path, token)
                    except Exception as e:
                        logger.exception("Lỗi xử lý Kafka message: %s", e)
            except Exception as e:
                if _running:
                    logger.warning("Kafka consumer lỗi/kết nối chưa sẵn sàng: %s. Thử lại sau 5 giây.", e)
                    time.sleep(5)
            finally:
                if consumer is not None:
                    consumer.close()

    _consumer_thread = threading.Thread(target=_run, daemon=True, name="kafka-consumer")
    _consumer_thread.start()


def stop_consumer():
    global _running
    _running = False
    logger.info("Kafka consumer đã dừng.")
