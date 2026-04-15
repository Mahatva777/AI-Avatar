#!/usr/bin/env python3
"""
Quick test — sends a WAV file to /analyze and prints the response.
Usage:  python test_server.py path/to/audio.wav
        python test_server.py          <- records 3s from mic and tests
"""
import sys, requests, wave, struct, math, time

SERVER = "http://127.0.0.1:8000"

def check_health():
    try:
        r = requests.get(f"{SERVER}/health", timeout=5)
        print("[Health]", r.json())
        return True
    except Exception as e:
        print(f"[ERROR] Server not reachable: {e}")
        return False

def send_wav(path):
    with open(path, "rb") as f:
        wav_bytes = f.read()
    print(f"Sending {path} ({len(wav_bytes)} bytes)...")
    t0 = time.time()
    r  = requests.post(f"{SERVER}/analyze",
                       files={"file": ("audio.wav", wav_bytes, "audio/wav")},
                       timeout=30)
    elapsed = time.time() - t0
    print(f"Response in {elapsed:.2f}s:")
    print(r.json())

def make_test_wav(filename="test_tone.wav", freq=440, duration=2, sr=16000):
    """Generates a simple sine-wave WAV for connectivity testing."""
    samples = [int(32767 * math.sin(2 * math.pi * freq * i / sr))
               for i in range(sr * duration)]
    with wave.open(filename, "w") as wf:
        wf.setnchannels(1); wf.setsampwidth(2); wf.setframerate(sr)
        wf.writeframes(struct.pack(f"{len(samples)}h", *samples))
    return filename

if __name__ == "__main__":
    if not check_health():
        print("Start the server first:  uvicorn server:app --host 127.0.0.1 --port 8000")
        sys.exit(1)

    if len(sys.argv) > 1:
        send_wav(sys.argv[1])
    else:
        print("No WAV provided — generating a test tone (no speech, expect neutral)...")
        path = make_test_wav()
        send_wav(path)
