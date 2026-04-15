#!/bin/bash
# Run this once to set up the Python server environment

echo "=== AI Avatar Server Setup ==="

# 1. Check Ollama
if ! command -v ollama &> /dev/null; then
    echo "Ollama not found. Installing..."
    curl -fsSL https://ollama.com/install.sh | sh
else
    echo "Ollama found: $(ollama --version)"
fi

# 2. Pull Llama3 if not present
echo "Pulling llama3 model (this may take a few minutes on first run)..."
ollama pull llama3

# 3. Python deps
echo "Installing Python dependencies..."
pip install -r requirements.txt

echo ""
echo "=== Setup complete! ==="
echo "Start the server with:"
echo "  cd python-server/src"
echo "  uvicorn server:app --host 127.0.0.1 --port 8000 --reload"
