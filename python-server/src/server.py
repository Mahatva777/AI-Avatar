import asyncio
import io
import json
import sys
import os

# Ensure model.py in same directory is importable
sys.path.insert(0, os.path.dirname(__file__))

import numpy as np
import soundfile as sf
import librosa
import torch
import ollama
from fastapi import FastAPI, File, UploadFile
from fastapi.responses import JSONResponse
from faster_whisper import WhisperModel

# ── CNN — optional ────────────────────────────────────────────────────────────
CNN_AVAILABLE = False
try:
    from model import CNNAudio
    _model_path = os.path.join(os.path.dirname(__file__), "..", "models", "cnn_mfcc.pth")
    checkpoint    = torch.load(_model_path, map_location="cpu")
    label_map     = checkpoint["label_map"]
    inv_label_map = {v: k for k, v in label_map.items()}
    cnn_model     = CNNAudio(len(label_map))
    cnn_model.load_state_dict(checkpoint["model_state_dict"])
    cnn_model.eval()
    CNN_AVAILABLE = True
    print("[server] CNN model loaded OK")
except Exception as e:
    print(f"[server] CNN not available ({e}) — running LLM-only mode")

app = FastAPI()

# ── Whisper ───────────────────────────────────────────────────────────────────
print("[server] Loading Whisper tiny...")
whisper_model = WhisperModel("tiny", device="cpu", compute_type="int8")
print("[server] Whisper ready")

EMOTIONS     = ["happy", "sad", "angry", "neutral"]
OLLAMA_MODEL = "llama3.1"

# ── Helpers ───────────────────────────────────────────────────────────────────
def extract_mfcc(wav, sr=16000, n_mfcc=40):
    mfcc = librosa.feature.mfcc(y=wav, sr=sr, n_mfcc=n_mfcc)
    mfcc = (mfcc - np.mean(mfcc)) / (np.std(mfcc) + 1e-6)
    return mfcc.astype(np.float32)

def cnn_predict(wav, sr):
    if not CNN_AVAILABLE:
        return "neutral", 0.5
    mfcc = extract_mfcc(wav, sr)
    if mfcc.shape[1] < 128:
        mfcc = np.pad(mfcc, ((0, 0), (0, 128 - mfcc.shape[1])))
    else:
        mfcc = mfcc[:, :128]
    tensor = torch.tensor(np.expand_dims(np.expand_dims(mfcc, 0), 0), dtype=torch.float32)
    with torch.no_grad():
        probs = torch.softmax(cnn_model(tensor), dim=1)[0]
        idx   = torch.argmax(probs).item()
    return inv_label_map.get(idx, "neutral"), float(probs[idx])

def llm_predict(transcript: str):
    if not transcript.strip():
        return "neutral", 0.5
    prompt = (
        "Classify the emotion in this speech transcript into exactly one of: "
        "happy, sad, angry, neutral.\n"
        f'Transcript: "{transcript}"\n'
        "Respond ONLY with valid JSON like: "
        '{"emotion": "happy", "confidence": 0.85}'
    )
    try:
        response = ollama.chat(
            model=OLLAMA_MODEL,
            messages=[{"role": "user", "content": prompt}],
            format="json",
            options={"temperature": 0.0, "num_predict": 60}
        )
        data       = json.loads(response["message"]["content"])
        emotion    = data.get("emotion", "neutral").lower().strip()
        confidence = float(data.get("confidence", 0.5))
        if emotion not in EMOTIONS:
            emotion = "neutral"
        return emotion, confidence
    except Exception as e:
        print(f"[LLM error] {e}")
        return "neutral", 0.4

def fuse(cnn_emotion, cnn_conf, llm_emotion, llm_conf):
    if not CNN_AVAILABLE:
        return llm_emotion, llm_conf
    scores = {e: 0.0 for e in EMOTIONS}
    scores[cnn_emotion] += cnn_conf * 0.40
    scores[llm_emotion] += llm_conf * 0.60
    best = max(scores, key=scores.__getitem__)
    conf = min(scores[best], 1.0)
    if cnn_emotion == llm_emotion:
        conf = min(conf * 1.15, 1.0)
    return best, conf

# ── Endpoints ─────────────────────────────────────────────────────────────────
@app.post("/analyze")
async def analyze(file: UploadFile = File(...)):
    data = await file.read()
    wav, sr = sf.read(io.BytesIO(data))
    if wav.ndim > 1:
        wav = wav.mean(axis=1)
    wav_16 = librosa.resample(wav, orig_sr=sr, target_sr=16000) if sr != 16000 else wav

    loop     = asyncio.get_event_loop()
    cnn_task = loop.run_in_executor(None, cnn_predict, wav_16, 16000)

    segments, _ = whisper_model.transcribe(io.BytesIO(data), beam_size=1, language="en")
    transcript  = " ".join(s.text for s in segments).strip()
    print(f"[Whisper] \"{transcript}\"")

    cnn_emotion, cnn_conf = await cnn_task
    llm_emotion, llm_conf = await loop.run_in_executor(None, llm_predict, transcript)
    print(f"[CNN] {cnn_emotion} ({cnn_conf:.2f})  [LLM] {llm_emotion} ({llm_conf:.2f})")

    final_emotion, final_conf = fuse(cnn_emotion, cnn_conf, llm_emotion, llm_conf)
    print(f"[FINAL] {final_emotion} ({final_conf:.2f})")

    return JSONResponse({
        "emotion":    final_emotion,
        "confidence": round(final_conf, 3),
        "transcript": transcript,
        "cnn":  {"emotion": cnn_emotion,  "confidence": round(cnn_conf, 3), "available": CNN_AVAILABLE},
        "llm":  {"emotion": llm_emotion,  "confidence": round(llm_conf, 3)},
    })

@app.post("/predict")
async def predict_legacy(file: UploadFile = File(...)):
    return await analyze(file)

@app.get("/health")
async def health():
    return {"status": "ok", "cnn": CNN_AVAILABLE, "whisper": True, "llm_model": OLLAMA_MODEL}
