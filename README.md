# AI-Avatar: Real-Time Conversational 3D Avatar

An end-to-end, ultra-low-latency pipeline for interactive 3D virtual avatars. It combines **Apple Silicon Metal GPU speech recognition**, **pretrained transformer-based emotion classification**, and **real-time VRoid blendshape morphing in Unity** to achieve spontaneous (<200ms) emotion synchronization.

[![FastAPI](https://img.shields.io/badge/FastAPI-0.100%2B-009688.svg?style=flat&logo=FastAPI&logoColor=white)](https://fastapi.tiangolo.com)
[![Unity](https://img.shields.io/badge/Unity-2022.3%20LTS-black.svg?style=flat&logo=unity)](https://unity.com)
[![Apple Silicon](https://img.shields.io/badge/Accelerated-Apple%20Metal%20GPU-grey.svg?style=flat&logo=apple)](https://developer.apple.com/metal/)
[![Latency](https://img.shields.io/badge/Latency-~150ms-brightgreen.svg?style=flat)]()
[![Accuracy](https://img.shields.io/badge/Accuracy-%3E90%25-blue.svg?style=flat)]()

---

## ⚡ Key Highlights

- **Sub-200ms Total Latency:** Real-time conversational responsiveness without awkward emotion lags.
- **Hardware Acceleration:** Uses `mlx-whisper` on Apple Silicon Metal GPU for 90–130ms speech-to-text.
- **High-Accuracy Emotion Recognition:** Uses `j-hartmann/emotion-english-distilroberta-base` (>90% accuracy across `happy`, `sad`, `angry`, `neutral`).
- **Complete Self-Contained Unity Client:** Includes VRoid VRM avatars (`a1.vrm`, `a2.vrm`), pre-configured scenes, audio recorders, blendshape drivers, and test clips.
- **Two Operation Modes:**
  1. **Instant Demo Mode:** Plays pre-recorded voice clips with zero microphone setup.
  2. **Live Microphone Mode:** Continuously records speech in 1-second chunks and streams expressions in real time.

---

## 🏗️ Architecture

```
User Voice / Test Clip (.wav)
          │
          ▼
┌─────────────────────────────────────────────────────────┐
│              FastAPI Backend (Port 8000)                │
│                                                         │
│  1. mlx-whisper (Apple Metal GPU)        ~100–130ms     │
│     Transcribes audio to text                           │
│                                                         │
│  2. DistilRoBERTa Emotion Transformer     ~10–15ms      │
│     Maps text -> happy, sad, angry, neutral             │
│                                                         │
│  3. Async Server-Sent Events (SSE)                      │
│     Pushes real-time updates over /emotion_stream       │
└─────────────────────────────────────────────────────────┘
          │
          ▼  ~150ms total
┌─────────────────────────────────────────────────────────┐
│               Unity 3D Client (URP)                     │
│                                                         │
│  • AudioSender & MicrophoneRecorder                     │
│  • EmotionControllerAdapter (Wall-clock hysteresis)     │
│  • EmotionController (Layered VRoid Face Blendshapes)   │
│  • VoiceClipTester (Instant Demo Clip Player)           │
└─────────────────────────────────────────────────────────┘
```

For complete benchmarking data and architectural comparisons, see [LATENCY_AND_ACCURACY_OPTIMIZATION.md](LATENCY_AND_ACCURACY_OPTIMIZATION.md).

---

## 📂 Repository Structure

```
AI-Avatar/
├── python-server/              # Backend service
│   ├── src/
│   │   ├── server.py           # FastAPI server (mlx-whisper + DistilRoBERTa + SSE)
│   │   └── model.py            # Legacy model definitions
│   ├── requirements.txt        # Python dependencies
│   └── setup.sh                # Environment setup script
├── unity-client/               # Complete, ready-to-open Unity project
│   ├── Assets/                 # Scenes, C# scripts, VRM avatars, sample audio clips
│   ├── Packages/               # Package manifest (UniGLTF, VRM 1.0, URP)
│   └── ProjectSettings/        # Project and input configurations
├── LATENCY_AND_ACCURACY_OPTIMIZATION.md  # Detailed technical optimization report
└── README.md                   # This file
```

---

## 🚀 Quickstart Guide

### 1. Prerequisites
- **Operating System:** macOS (Apple Silicon recommended for Metal acceleration; automatically falls back to CPU on Intel/Linux/Windows).
- **Python:** 3.10 or newer (tested on Python 3.12).
- **Unity:** Unity 2022.3 LTS (or compatible newer version) with Universal Render Pipeline (URP).

---

### 2. Set Up the Python Backend

1. **Clone the repository:**
   ```bash
   git clone https://github.com/Mahatva777/AI-Avatar.git
   cd AI-Avatar/python-server
   ```

2. **Create and activate a virtual environment:**
   ```bash
   python3 -m venv .venv
   source .venv/bin/activate
   ```

3. **Install dependencies:**
   ```bash
   pip install -r requirements.txt
   ```
   *(Note: On Apple Silicon, `mlx-whisper` compiles automatically to utilize the Metal GPU.)*

4. **Launch the server:**
   ```bash
   cd src
   uvicorn server:app --host 127.0.0.1 --port 8000
   ```

   You should see:
   ```text
   [server] mlx-whisper detected — using Apple Metal GPU for speech recognition
   [server] mlx-whisper warmed up ✓
   [server] Emotion classifier warmed up ✓
   INFO:     Uvicorn running on http://127.0.0.1:8000
   ```

5. **Verify server health (in another terminal):**
   ```bash
   curl http://127.0.0.1:8000/health
   ```
   *Returns `{"status": "ok", "whisper_backend": "mlx", ...}`.*

---

### 3. Open and Run the Unity Client

1. Open **Unity Hub**.
2. Click **Add** > **Add project from disk** and select the `unity-client` folder inside this repository:
   ```
   AI-Avatar/unity-client
   ```
3. Open the project in Unity Editor.
4. In the Project window (bottom), open the main scene:
   ```
   Assets/Scenes/SampleScene.unity
   ```

---

### 4. Running the Demos

#### Option A: Instant Demo (Pre-recorded Voice Clips — No Mic Required)
1. Ensure the Python server is running on `http://127.0.0.1:8000`.
2. Click the **Play (▶)** button at the top of the Unity Editor.
3. The avatar will automatically speak 4 sample voice clips (`happy`, `angry`, `sad`, `neutral`) through your speakers.
4. In under 200ms, the avatar's face will smoothly morph into the corresponding expression.

#### Option B: Live Microphone Mode (Talk to the Avatar)
1. In Unity Hierarchy, select the **Avatar** GameObject.
2. In the Inspector on the right, **uncheck the box next to `VoiceClipTester`** to disable it.
3. Make sure the `AudioManager` GameObject has `AudioSender` enabled.
4. Click **Play (▶)** and speak into your microphone:
   - Your speech is captured in 1-second chunks.
   - The avatar morphs facial blendshapes in real time to match your emotion and tone!

---

## 📊 Performance Benchmarks

| Metric | Before Optimization | After Optimization | Improvement |
| :--- | :--- | :--- | :--- |
| **Whisper Transcription** | 2,000–3,000ms (CPU) | **90–130ms (Metal GPU)** | **20× faster** |
| **Emotion Inference** | 20,000–70,000ms (Ollama 8B) | **10–15ms (DistilRoBERTa)** | **>2,000× faster** |
| **Unity Expression Filter** | ~25,000ms (Frame-delta bug) | **<16ms (Wall-clock Time)** | Instant |
| **Total Round-Trip Latency** | **~25,000–75,000ms** | **~150–200ms** | **>100× faster** |
| **Emotion Accuracy** | ❌ ~25% (Class collapse) | ✅ **>90%** across 4 emotions | Fully working |

---

## 📡 API Endpoints

### `POST /analyze`
Primary inference endpoint. Takes a WAV audio file, transcribes via Whisper, classifies emotion via DistilRoBERTa, and returns immediately.
- **Request:** `multipart/form-data` with `file: audio.wav`
- **Response:**
  ```json
  {
    "request_id": "8530247d",
    "emotion": "happy",
    "confidence": 0.988,
    "transcript": "I'm happy to hear that.",
    "source": "fast_transformer",
    "scores": {
      "joy": 0.988,
      "surprise": 0.007,
      "neutral": 0.002,
      "sadness": 0.002,
      "anger": 0.001,
      "disgust": 0.001,
      "fear": 0.0
    }
  }
  ```

### `GET /emotion_stream`
Server-Sent Events (SSE) streaming endpoint. Unity clients subscribe once at startup to receive asynchronous emotion updates pushed in real time.

### `GET /health`
Returns system status, active Whisper backend, model names, and connected SSE client count.

---

## 🛠️ Tech Stack & Credits

- **Backend:** [FastAPI](https://fastapi.tiangolo.com/), [mlx-whisper](https://github.com/ml-explore/mlx-examples/tree/main/whisper), [Transformers](https://github.com/huggingface/transformers) (`j-hartmann/emotion-english-distilroberta-base`), [PyTorch](https://pytorch.org/), [Librosa](https://librosa.org/).
- **Frontend / Client:** [Unity](https://unity.com) 2022.3 LTS (URP), [UniVRM](https://github.com/vrm-c/UniVRM), VRoid Studio avatar blendshapes.

---

## 📄 License

MIT License. See [LICENSE](LICENSE) for details.
