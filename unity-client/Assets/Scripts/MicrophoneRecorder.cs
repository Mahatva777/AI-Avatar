using System.Collections;
using UnityEngine;

public class MicrophoneRecorder : MonoBehaviour
{
    [Header("Settings")]
    public int   sampleRate  = 16000;
    public float clipSeconds = 2f;
    [Tooltip("Leave blank for auto-select. Type keyword like 'macbook' to force.")]
    public string preferredMic = "";

    // ── public state ──────────────────────────────────────────────────────────
    public AudioClip ReadyClip  { get; private set; } = null;
    public bool      IsRecording { get; private set; } = false;

    private string _mic = "";

    private static readonly string[] SKIP_KEYWORDS = {
        "screen", "virtual", "loopback", "stereo mix", "transcreen",
        "soundflower", "blackhole", "cable", "monitor", "display"
    };

    // ── lifecycle ─────────────────────────────────────────────────────────────
    void Start()
    {
        Debug.Log("[Mic] ===== MicrophoneRecorder Start =====");

        if (Microphone.devices.Length == 0)
        {
            Debug.LogError("[Mic] No microphone devices found! Check macOS Privacy -> Microphone.");
            return;
        }

        Debug.Log($"[Mic] {Microphone.devices.Length} device(s) found:");
        foreach (var d in Microphone.devices)
            Debug.Log($"[Mic]   -> \"{d}\"");

        // 1. Preferred keyword
        if (!string.IsNullOrEmpty(preferredMic))
            foreach (var d in Microphone.devices)
                if (d.ToLower().Contains(preferredMic.ToLower()))
                { _mic = d; Debug.Log($"[Mic] Preferred match: {_mic}"); break; }

        // 2. Auto-skip virtual
        if (string.IsNullOrEmpty(_mic))
            foreach (var d in Microphone.devices)
            {
                bool skip = false;
                foreach (var kw in SKIP_KEYWORDS)
                    if (d.ToLower().Contains(kw)) { skip = true; break; }
                if (!skip) { _mic = d; Debug.Log($"[Mic] Auto-selected: {_mic}"); break; }
            }

        // 3. Fallback to first
        if (string.IsNullOrEmpty(_mic))
        {
            _mic = Microphone.devices[0];
            Debug.LogWarning($"[Mic] Fallback to first device: {_mic}");
        }

        StartCoroutine(RecordLoop());
    }

    // ── record loop ───────────────────────────────────────────────────────────
    IEnumerator RecordLoop()
    {
        int loopCount = 0;
        while (true)
        {
            loopCount++;
            ReadyClip = null;
            IsRecording = true;

            Debug.Log($"[Mic] Loop #{loopCount}: starting {clipSeconds}s recording on '{_mic}'...");

            // Start a looping clip (avoids IsRecording() stuck bug on macOS)
            AudioClip clip = Microphone.Start(_mic, true, Mathf.CeilToInt(clipSeconds) + 1, sampleRate);

            if (clip == null)
            {
                Debug.LogError("[Mic] Microphone.Start() returned null — mic unavailable.");
                IsRecording = false;
                yield return new WaitForSeconds(1f);
                continue;
            }

            // ── Wait for mic to BEGIN writing (timeout 5s) ───────────────────
            float waitStart = Time.realtimeSinceStartup;
            while (Microphone.GetPosition(_mic) <= 0)
            {
                if (Time.realtimeSinceStartup - waitStart > 5f)
                {
                    Debug.LogError("[Mic] Timed out waiting for mic to start. " +
                                   "Check macOS Settings -> Privacy -> Microphone -> allow Unity.");
                    Microphone.End(_mic);
                    IsRecording = false;
                    yield return new WaitForSeconds(2f);
                    goto NextLoop;
                }
                yield return null;
            }
            Debug.Log($"[Mic] Mic started writing. Position = {Microphone.GetPosition(_mic)}");

            // ── Record for clipSeconds ───────────────────────────────────────
            yield return new WaitForSeconds(clipSeconds);

            int pos = Microphone.GetPosition(_mic);
            Microphone.End(_mic);
            IsRecording = false;

            Debug.Log($"[Mic] Loop #{loopCount}: captured {pos} samples.");

            if (pos <= 0)
            {
                Debug.LogWarning("[Mic] 0 samples — mic is silent. Is it muted?");
                yield return new WaitForSeconds(0.5f);
                goto NextLoop;
            }

            // ── Trim to actual length ────────────────────────────────────────
            float[] data = new float[pos * clip.channels];
            clip.GetData(data, 0);
            AudioClip trimmed = AudioClip.Create("mic_chunk", pos, clip.channels, sampleRate, false);
            trimmed.SetData(data, 0);
            ReadyClip = trimmed;

            Debug.Log($"[Mic] Clip ready: {pos} samples, {clip.channels}ch, {sampleRate}Hz.");

            yield return new WaitForSeconds(0.05f);   // tiny gap before next loop
            NextLoop:;
        }
    }
}
