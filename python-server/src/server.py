
from fastapi import FastAPI, File, UploadFile
import torch
import librosa
import numpy as np
import io
import soundfile as sf
from model import CNNAudio

app = FastAPI()

checkpoint = torch.load("../models/cnn_mfcc.pth", map_location="cpu")
label_map = checkpoint["label_map"]
inv_label_map = {v:k for k,v in label_map.items()}

model = CNNAudio(len(label_map))
model.load_state_dict(checkpoint["model_state_dict"])
model.eval()

def extract_mfcc(wav, sr=16000, n_mfcc=40):
    mfcc = librosa.feature.mfcc(y=wav, sr=sr, n_mfcc=n_mfcc)
    mfcc = (mfcc - np.mean(mfcc)) / (np.std(mfcc) + 1e-6)
    return mfcc.astype(np.float32)

@app.post("/predict")
async def predict(file: UploadFile = File(...)):
    data = await file.read()
    wav, sr = sf.read(io.BytesIO(data))
    if wav.ndim > 1:
        wav = wav.mean(axis=1)
    mfcc = extract_mfcc(wav, sr)

    if mfcc.shape[1] < 128:
        pad = 128 - mfcc.shape[1]
        mfcc = np.pad(mfcc, ((0,0),(0,pad)))
    else:
        mfcc = mfcc[:, :128]

    mfcc = np.expand_dims(np.expand_dims(mfcc,0),0)
    tensor = torch.tensor(mfcc, dtype=torch.float32)

    with torch.no_grad():
        out = model(tensor)
        probs = torch.softmax(out, dim=1)[0]
        idx = torch.argmax(probs).item()

    return {"emotion": inv_label_map[idx], "confidence": float(probs[idx])}
