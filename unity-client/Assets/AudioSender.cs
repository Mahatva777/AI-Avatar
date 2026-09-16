// ─────────────────────────────────────────────────────────────────────────────
// AudioSender — Optimized for sub-300ms latency
//
// Changes from original:
//  • sendInterval reduced to 0.25s (was 1.0s)
//  • Adds EmotionStreamListener coroutine:
//      - Connects to GET /emotion_stream (SSE) at startup
//      - Parses LLM-enriched emotion pushes from server
//      - Feeds them to emotionAdapter with source="llm"
//  • SetTestClip() injection for VoiceClipTester still works
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[RequireComponent(typeof(MicrophoneRecorder))]
public class AudioSender : MonoBehaviour
{
    [Header("References")]
    public MicrophoneRecorder recorder;
    public EmotionControllerAdapter emotionAdapter;

    [Header("Server")]
    public string serverUrl       = "http://127.0.0.1:8000/analyze";
    public string sseStreamUrl    = "http://127.0.0.1:8000/emotion_stream";

    [Header("Timing")]
    [Tooltip("How often to poll the microphone and send audio (seconds). 0.25 recommended.")]
    public float sendInterval = 0.25f;

    private AudioClip _lastSent  = null;
    private int       _pollCount = 0;
    private bool      _sending   = false;

    // ── test-mode injection (unchanged) ──────────────────────────────────────
    private AudioClip _testClip         = null;
    private bool      _testClipConsumed = true;

    /// <summary>
    /// Called by VoiceClipTester to inject a pre-loaded AudioClip directly,
    /// bypassing the microphone. The clip is sent to /analyze exactly once.
    /// </summary>
    public void SetTestClip(AudioClip clip)
    {
        _testClip         = clip;
        _testClipConsumed = false;
        Debug.Log($"[Sender] Test clip injected: {clip.name}");
    }

    void Start()
    {
        Debug.Log("[Sender] ===== AudioSender Start =====");

        if (recorder == null) recorder = GetComponent<MicrophoneRecorder>();
        if (recorder == null) { Debug.LogError("[Sender] MicrophoneRecorder NOT found!"); return; }
        Debug.Log($"[Sender] Recorder: {recorder.name}");

        if (emotionAdapter == null)
            emotionAdapter = FindFirstObjectByType<EmotionControllerAdapter>();
        if (emotionAdapter == null) Debug.LogWarning("[Sender] EmotionControllerAdapter not found.");
        else Debug.Log($"[Sender] Adapter: {emotionAdapter.name}");

        StartCoroutine(PollLoop());
        StartCoroutine(EmotionStreamListener());
    }

    // ── Fast poll loop — sends audio every sendInterval ──────────────────────
    IEnumerator PollLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(sendInterval);
            _pollCount++;

            // Prefer injected test clip
            AudioClip clip;
            if (_testClip != null && !_testClipConsumed)
            {
                clip              = _testClip;
                _testClipConsumed = true;
                Debug.Log($"[Sender] Poll #{_pollCount}: using injected test clip '{clip.name}'");
            }
            else
            {
                clip = recorder.ReadyClip;
                bool recording = recorder.IsRecording;
                bool hasClip   = clip != null;
                bool isNew     = clip != _lastSent;

                Debug.Log($"[Sender] Poll #{_pollCount}: recording={recording} hasClip={hasClip} isNew={isNew} sending={_sending}");
                if (!hasClip || !isNew || _sending) continue;
            }

            if (_sending) continue;

            _lastSent = clip;
            _sending  = true;

            byte[] wav = WavUtility.FromAudioClip(clip);
            if (wav == null || wav.Length < 100)
            {
                Debug.LogWarning($"[Sender] WAV too small ({wav?.Length ?? 0} bytes). Skipping.");
                _sending = false;
                continue;
            }

            Debug.Log($"[Sender] Sending {wav.Length} bytes to {serverUrl}...");
            yield return StartCoroutine(PostAudio(wav));
            _sending = false;
        }
    }

    // ── POST audio — now receives CNN-fast result only ────────────────────────
    IEnumerator PostAudio(byte[] wav)
    {
        WWWForm form = new WWWForm();
        form.AddBinaryData("file", wav, "audio.wav", "audio/wav");

        using UnityWebRequest req = UnityWebRequest.Post(serverUrl, form);
        req.timeout = 10; // reduced from 20s — CNN result should be <2s

        float t0 = Time.realtimeSinceStartup;
        yield return req.SendWebRequest();
        float elapsed = Time.realtimeSinceStartup - t0;

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[Sender] HTTP error ({elapsed:F2}s): {req.error}");
            yield break;
        }

        string json = req.downloadHandler.text;
        Debug.Log($"[Sender] /analyze response ({elapsed:F2}s): {json}");

        EmotionResponse r = JsonUtility.FromJson<EmotionResponse>(json);
        if (r == null) { Debug.LogWarning("[Sender] JSON parse failed."); yield break; }

        Debug.Log($"[Sender] CNN fast => emotion={r.emotion} conf={r.confidence:F2} source={r.source}");

        // Apply CNN result immediately (fast path)
        if (emotionAdapter != null)
            emotionAdapter.ReceiveEmotion(r.emotion, r.confidence, "cnn");
    }

    // ── SSE Listener — receives LLM-enriched emotions from /emotion_stream ────
    /// <summary>
    /// Opens a persistent GET connection to the server's SSE endpoint.
    /// Whenever the server finishes an LLM inference, it pushes a JSON event.
    /// We parse it and apply the fused emotion on top of the current CNN result.
    /// Reconnects automatically on disconnect.
    /// </summary>
    IEnumerator EmotionStreamListener()
    {
        Debug.Log($"[SSE] Connecting to {sseStreamUrl}...");

        while (true) // auto-reconnect loop
        {
            using UnityWebRequest req = UnityWebRequest.Get(sseStreamUrl);
            req.timeout = 0; // no timeout — persistent connection
            req.SetRequestHeader("Accept", "text/event-stream");
            req.SetRequestHeader("Cache-Control", "no-cache");

            var downloadHandler = new DownloadHandlerBuffer();
            req.downloadHandler = downloadHandler;

            req.SendWebRequest();

            int lastByteRead = 0;

            while (!req.isDone)
            {
                yield return new WaitForSeconds(0.1f); // check every 100ms

                if (req.result == UnityWebRequest.Result.ConnectionError ||
                    req.result == UnityWebRequest.Result.ProtocolError)
                {
                    Debug.LogWarning($"[SSE] Connection error: {req.error}. Reconnecting in 2s...");
                    break;
                }

                // Read new bytes since last check
                byte[] bytes = downloadHandler.data;
                if (bytes == null || bytes.Length <= lastByteRead) continue;

                string newChunk = Encoding.UTF8.GetString(bytes, lastByteRead, bytes.Length - lastByteRead);
                lastByteRead = bytes.Length;

                // SSE lines look like: "data: {...json...}\n\n"
                foreach (string rawLine in newChunk.Split('\n'))
                {
                    string line = rawLine.Trim();
                    if (!line.StartsWith("data:")) continue;

                    string jsonStr = line.Substring(5).Trim();
                    if (string.IsNullOrEmpty(jsonStr) || jsonStr.StartsWith("{\"event\":\"connected\""))
                    {
                        Debug.Log("[SSE] Connected to emotion stream ✓");
                        continue;
                    }

                    try
                    {
                        EmotionStreamEvent ev = JsonUtility.FromJson<EmotionStreamEvent>(jsonStr);
                        if (ev != null && !string.IsNullOrEmpty(ev.emotion))
                        {
                            Debug.Log($"[SSE] LLM-fused => emotion={ev.emotion} conf={ev.confidence:F2} transcript=\"{ev.transcript}\"");
                            if (emotionAdapter != null)
                                emotionAdapter.ReceiveEmotion(ev.emotion, ev.confidence, "llm");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[SSE] Parse error: {ex.Message} | raw: {jsonStr}");
                    }
                }
            }

            Debug.Log("[SSE] Disconnected. Reconnecting in 2s...");
            yield return new WaitForSeconds(2f);
        }
    }
}

// ── Response models ───────────────────────────────────────────────────────────
[System.Serializable]
public class EmotionResponse
{
    public string emotion;
    public float  confidence;
    public string transcript;
    public string source;      // "cnn_fast" or "llm_fused"
    public string request_id;
}

[System.Serializable]
public class EmotionStreamEvent
{
    public string emotion;
    public float  confidence;
    public string transcript;
    public string source;
    public string request_id;
}
