FROM python:3.10-slim

WORKDIR /app

ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libgl1 \
        libglib2.0-0 \
        libgomp1 \
        libsm6 \
        libxext6 \
        libxrender1 \
        poppler-utils \
    && rm -rf /var/lib/apt/lists/*

COPY OCRService/requirements.txt ./requirements.txt
RUN pip install --no-cache-dir -r requirements.txt

COPY OCRService/app ./app

EXPOSE 5051

CMD ["uvicorn", "app.main:app", "--host", "0.0.0.0", "--port", "5051"]

