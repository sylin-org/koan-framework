FROM python:3.11-slim

# Install pinned Pandoc + XeTeX dependencies for deterministic rendering
RUN apt-get update && apt-get install -y \
    pandoc=3.1.11-1 \
    texlive-xetex=2024.20240313-1 \
    texlive-fonts-recommended=2024.20240313-1 \
    fonts-dejavu=2.37-6 \
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
