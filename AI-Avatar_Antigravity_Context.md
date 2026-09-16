# AI-Powered Digital Avatar — Full Project Context for Antigravity

## Purpose of This Document

This document gives complete architectural, implementation, and optimization context for the **AI-Powered Digital Avatar with Real-Time Voice-Based Emotion Recognition** project. Use it to understand the system end-to-end before making any changes, and specifically to guide **latency optimization** work.

Repo: `github.com/Mahatva777/AI-Avatar`

---

## 1. Project Summary

A Unity 6 client captures live microphone audio, sends it to a Python FastAPI backend every ~2 seconds. The backend runs **two parallel inference paths** — a CNN on MFCC audio features, and a Whisper→LLM (Ollama LLaMA 3.1) text path — fuses their outputs into a single emotion label, and returns it. The Unity client then smooths and applies this emotion to a VRoid avatar's VRM blendshapes.

Four emotion classes: `happy`, `sad`, `angry`, `neutral`.

Target: sub-800ms round-trip latency, 30+ FPS in Unity during inference, no visible flicker in avatar expression.

---

## 2. Complete Data Flow

```
[Unity 6 Client]
Microphone (16kHz)
  → MicrophoneRecorder.cs   — records 2s clip via WaitForSeconds timer
                               (NOT Microphone.IsRecording(), which hangs on macOS Metal)
  → WavUtility.cs           — converts AudioClip → 16-bit PCM WAV byte array (44-byte header)
  → AudioSender.cs          — polls every 1s, POSTs multipart/form-data to /analyze

[Python FastAPI Backend — server.py]
  ├── CNN path (parallel, via asyncio run_in_executor):
  │     Librosa: extract 40 MFCC coefficients → normalize → pad/truncate to 128 frames
  │     → CNNAudio (3x Conv2D+ReLU+MaxPool → AdaptiveAvgPool → 2x FC) → softmax → (emotion, confidence)
  │
  └── LLM path (parallel):
        faster-whisper "tiny" (int8, CPU) → transcript
        → HTTP POST to Ollama (local OR remote via ngrok tunnel) /api/generate
        → LLaMA 3.1, temperature=0, JSON-constrained output → (emotion, confidence)

  Fusion: weighted sum — CNN weight 0.40, LLM weight 0.60
           if both paths agree → confidence *= 1.15 (capped at 1.0)
           if CNN model unavailable → fallback to LLM-only

  Response: JSON { emotion, confidence, transcript, cnn: {...}, llm: {...} }

[Unity 6 Client — receiving]
  → EmotionControllerAdapter.cs — hysteresis filter: emotion must persist 0.4s
                                    before being accepted; confidence threshold 0.35;
                                    reverts to neutral below threshold
  → EmotionController.cs        — applies VRM blendshape weights via UniVRM
                                    BlendShapeProxy, Lerp-smoothed frame-by-frame
  → VRoid Avatar (VRM format)   — visible facial expression change
```

---

## 3. Key Architectural Decisions (and Why)

| Decision | Rationale |
|---|---|
| Dual-path CNN + LLM instead of single model | CNN alone struggles with short/noisy audio; LLM alone has ASR latency and misses paralinguistic cues (tone, pitch). Fusion improves robustness. |
| Late fusion (weighted average) over early/feature fusion | Simpler, avoids retraining, allows either path to run/fail independently, keeps CNN and LLM decoupled and swappable. |
| LLM weight (0.60) > CNN weight (0.40) | Transcript-level context judged more reliable for short utterances than raw MFCC classification, based on informal testing. |
| CNN and LLM run concurrently via `asyncio.run_in_executor` | Two independent, CPU-bound tasks — running sequentially would nearly double latency. |
| Whisper "tiny" model, int8 quantized, CPU-only | Avoids GPU dependency (target: consumer/dev hardware, eventually VR headset companion PC). Larger Whisper models add latency disproportionate to accuracy gain for short clips. |
| Ollama (local/self-hosted LLM) instead of cloud API (OpenAI, etc.) | No per-request cost, no external network dependency for privacy-sensitive audio-derived transcripts, avoids rate limits during dev/testing. |
| 2-second audio window | Balance between having enough signal for both MFCC and Whisper to work reliably, vs. responsiveness. Shorter clips reduce Whisper transcription accuracy; longer clips add latency and reduce perceived real-time-ness. |
| Hysteresis filter (0.4s persistence + confidence threshold 0.35) in `EmotionControllerAdapter` | Raw per-request predictions are noisy — without this, the avatar's face flickers between expressions unnaturally on every poll cycle. |
| `WaitForSeconds` timer instead of `Microphone.IsRecording()` in `MicrophoneRecorder.cs` | `IsRecording()` has a known hang bug on macOS with Metal graphics backend; the timer approach sidesteps it entirely. |
| VRM/VRoid avatar format + UniVRM | Standardized blendshape naming (`BlendShapeProxy`) makes emotion-to-face mapping portable and avoids hand-rigging a custom avatar. |
| FastAPI + Uvicorn (not Flask) | Native `async`/`await` support required for concurrent CNN+LLM execution without blocking. |
| Graceful degradation: CNN optional | If `cnn_mfcc.pth` checkpoint is missing/fails to load, system falls back to LLM-only mode rather than crashing — keeps the pipeline demoable even without a trained model. |
| Ollama accessed via ngrok tunnel (dev-time) | Backend and Ollama sometimes run on separate machines during development (e.g., Ollama on a beefier machine); ngrok provides a quick HTTPS tunnel without VPN/port-forwarding setup. This is a temporary dev pattern, not the target production architecture. |

---

## 4. Current Known Bottlenecks (Latency Sources)

List these explicitly for Antigravity to reason about, ranked by suspected impact:

1. **Whisper transcription time** — even "tiny" + int8 on CPU can take several hundred ms depending on host machine; this runs serially before the LLM call can start (LLM depends on transcript).
2. **Ollama LLM inference over network hop (ngrok)** — adds round-trip network latency on top of local inference time; ngrok free tier also has cold-start / rate-limit behavior.
3. **Sequential dependency: Whisper → LLM** — unlike CNN (which runs fully in parallel), the LLM path cannot start until Whisper finishes, meaning total LLM-path latency = Whisper time + Ollama time, not max(Whisper, Ollama).
4. **2-second fixed audio window + 1-second polling interval in `AudioSender`** — this alone imposes a floor on responsiveness independent of inference speed; user perceives lag even if inference is instant.
5. **HTTP overhead per request (multipart form upload)** — WAV re-encoded and sent fresh every poll cycle, no persistent connection/streaming.
6. **CNN inference** — currently the fastest path (no network, no ASR), but MFCC extraction (Librosa) is CPU-bound and not benchmarked yet.
7. **No caching / no streaming ASR** — Whisper processes the full 2s clip from scratch every cycle rather than incrementally.

---

## 5. Tech Stack (Exact Versions/Tools)

**Unity Client**
- Unity 6 LTS (6000.3.9f1)
- UniVRM (VRM import + BlendShapeProxy)
- VRoid Studio (avatar authoring)
- C# scripts: `MicrophoneRecorder.cs`, `WavUtility.cs`, `AudioSender.cs`, `EmotionControllerAdapter.cs`, `EmotionController.cs`

**Backend**
- Python 3.11, FastAPI, Uvicorn
- Librosa, SoundFile — audio decode/resample/MFCC
- PyTorch, NumPy — CNN training/inference
- faster-whisper ("tiny", int8, CPU) — ASR
- Ollama (LLaMA 3.1) — local or remote via ngrok, accessed via `httpx` REST calls to `/api/generate` (not the `ollama` Python library, to support remote hosts)
- Communication: HTTP REST, multipart/form-data WAV upload; `/analyze`, `/predict` (legacy alias), `/health` endpoints

**Model**
- `CNNAudio`: 3× (Conv2D + ReLU + MaxPool) → AdaptiveAvgPool → 2× FC layers, ~127K parameters total
- Trained on 40-coefficient MFCCs, 128-frame windows, CrossEntropyLoss, Adam (lr=1e-3), 5 epochs
- Checkpoint: `models/cnn_mfcc.pth`, includes `label_map` for class resolution

---

## 6. Non-Functional Requirements (Current Targets)

| Requirement | Target |
|---|---|
| Round-trip latency | < 800 ms |
| Unity frame rate during inference | ≥ 30 FPS |
| CNN availability | Graceful degradation to LLM-only if model absent |
| Emotion stability | No visible flickering; minimum 0.4s persistence before switching |
| Privacy | No raw audio stored server-side; prefer local-only LLM inference |
| Platform | macOS, Windows (Unity Editor + Python 3.11) |

---

## 7. What This Project Deliberately Does NOT Include

Important for Antigravity to avoid suggesting features that don't exist / aren't planned as "already built":

- No camera-based facial capture (headset occlusion makes this infeasible — this is the core premise of the project)
- No lip-sync animation (audio energy-based mouth movement is NOT implemented, despite appearing in early planning docs)
- No keyword-based rule engine for emotion detection (superseded by the LLM path)
- No cloud LLM API (OpenAI, Anthropic, etc.) — Ollama only
- No production-grade multi-user support — currently single-user, single-session
- No Quest 2/VR headset deployment yet — currently Unity Editor + desktop only (Quest 2 integration is a separate planned phase, not yet built)
- No ONNX/Unity Sentis on-device inference yet (currently server round-trip only) — this is explicitly future work
- No streaming/incremental ASR — full-clip batch transcription only

---

## 8. Optimization Goals for This Session

When engaging with this project, prioritize (in this order unless told otherwise):

1. **Reduce end-to-end round-trip latency** below the current 800ms target, focusing on:
   - Parallelizing or shortcutting the Whisper→LLM serial dependency
   - Evaluating whether Ollama should be local instead of ngrok-tunneled for production/demo scenarios
   - Reducing audio window size or polling interval without degrading Whisper/CNN accuracy
   - Reusing HTTP connections (`httpx.Client` persistent session vs. per-request client) instead of creating a new client per request
   - Benchmarking MFCC extraction and CNN inference time in isolation to confirm it's not a hidden bottleneck
2. **Improve emotion classification robustness** without adding classes yet (stay within happy/sad/angry/neutral).
3. **Prepare groundwork for future ONNX/Sentis on-device inference** (per the Future Scope in the research paper) — flag any code decisions that would block this migration later.
4. **Maintain graceful degradation** — no optimization should remove the CNN-unavailable or LLM-failure fallback paths.

---

## 9. Open Questions Antigravity Should Ask Before Making Changes

- Is Ollama expected to run locally (same machine as FastAPI) in the target deployment, or always remote/tunneled?
- Is the 2-second audio window a hard product requirement, or open to tuning for latency?
- Should latency optimization prioritize perceived responsiveness (UI/animation smoothing) or actual measured round-trip time?
- Is GPU acceleration available on the target deployment machine, or must CPU-only inference remain a constraint?
