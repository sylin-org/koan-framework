FROM python:3.11-slim

# Install Pandoc + XeTeX dependencies (use distro-provided versions)
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
    pandoc \
    texlive-xetex \
    texlive-fonts-recommended \
    fonts-dejavu \
    && rm -rf /var/lib/apt/lists/*

# Disable LaTeX shell escape
RUN echo "shell_escape = f" > /etc/texmf/texmf.d/95-no-shell-escape.cnf \
    && update-texmf

WORKDIR /app

COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt

COPY app.py ./

EXPOSE 7070

CMD ["uvicorn", "app:app", "--host", "0.0.0.0", "--port", "7070"]
