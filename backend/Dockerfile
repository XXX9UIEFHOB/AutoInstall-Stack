FROM python:3.12-slim

WORKDIR /app
ENV PYTHONUNBUFFERED=1 TZ=Asia/Shanghai

COPY server.py ./
COPY data ./data
COPY static ./static

EXPOSE 3809
ENTRYPOINT ["python", "server.py"]
CMD ["--host", "0.0.0.0", "--port", "3809", "--token", "change-me"]
