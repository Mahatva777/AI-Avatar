using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// VoiceClipTester
/// - In Test Mode (when this component is enabled), automatically pauses realtime mic recording.
/// - Plays pre-recorded audio clips through AudioSource.
/// - Sends the audio to /analyze (which responds in ~100ms via Apple Metal GPU acceleration).
/// - Applies blendshape emotions immediately on the avatar.
/// - If you disable or remove this component, realtime mic (MicrophoneRecorder + AudioSender) takes over.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class VoiceClipTester : MonoBehaviour
{
    [Header("Test Clips  (drag .wav AudioClips here)")]
    public List<AudioClip> testClips = new List<AudioClip>();

    [Header("Playback")]
    public float pauseBetweenClips = 1.2f;
    public bool  autoAdvance       = true;
    public bool  loop              = false;

    [Header("Server")]
    public string serverUrl      = "http://127.0.0.1:8000/analyze";
    public int    timeoutSeconds = 10;

    [HideInInspector] public EmotionController emotionController;

    private AudioSource _audioSource;
    private int  _currentIndex = -1;
    private bool _playing      = false;

    // ── Lifecycle ──────────────────────────────────────────────────────────────
    void Start()
    {
        _audioSource             = GetComponent<AudioSource>();
        _audioSource.playOnAwake = false;

        // In test mode, disable live microphone streaming so they do not conflict
        var mic    = FindFirstObjectByType<MicrophoneRecorder>();
        var sender = FindFirstObjectByType<AudioSender>();
        if (mic    != null) { mic.enabled    = false; Debug.Log("[Tester] MicrophoneRecorder paused for test mode."); }
        if (sender != null) { sender.enabled = false; Debug.Log("[Tester] AudioSender paused for test mode."); }

        // Find EmotionController across scene
        emotionController = FindFirstObjectByType<EmotionController>();
        if (emotionController == null)
            Debug.LogError("[Tester] EmotionController NOT found! Make sure it exists on avatar's Face object.");
        else
            Debug.Log($"[Tester] Connected to EmotionController on: '{emotionController.gameObject.name}'");

        if (testClips == null || testClips.Count == 0)
        {
            Debug.LogWarning("[Tester] No test clips assigned. Drag .wav AudioClips into the Test Clips list.");
            return;
        }

        Debug.Log($"[Tester] Ready with {testClips.Count} clip(s). AutoAdvance={autoAdvance}.");

        if (autoAdvance)
            StartCoroutine(AutoPlayAll());
        else
            Debug.Log("[Tester] Press [Space] to step through clips manually.");
    }

    void OnDisable()
    {
        // Re-enable live microphone if VoiceClipTester is disabled in Inspector
        var mic    = FindFirstObjectByType<MicrophoneRecorder>();
        var sender = FindFirstObjectByType<AudioSender>();
        if (mic    != null) mic.enabled    = true;
        if (sender != null) sender.enabled = true;
    }

    void Update()
    {
        if (!autoAdvance && Input.GetKeyDown(KeyCode.Space) && !_playing)
            StartCoroutine(PlayNextClip());
    }

    // ── Sequencing ────────────────────────────────────────────────────────────

    IEnumerator AutoPlayAll()
    {
        foreach (AudioClip clip in testClips)
        {
            if (clip == null) continue;
            yield return StartCoroutine(PlayClip(clip));
            yield return new WaitForSeconds(pauseBetweenClips);
        }
        if (loop)
        {
            StartCoroutine(AutoPlayAll());
        }
        else
        {
            Debug.Log("[Tester] All clips finished.");
        }
    }

    IEnumerator PlayNextClip()
    {
        if (testClips == null || testClips.Count == 0) yield break;
        _currentIndex = (_currentIndex + 1) % testClips.Count;
        if (testClips[_currentIndex] != null)
            yield return StartCoroutine(PlayClip(testClips[_currentIndex]));
    }

    // ── Core ──────────────────────────────────────────────────────────────────

    IEnumerator PlayClip(AudioClip clip)
    {
        _playing = true;
        Debug.Log($"[Tester] ▶ Playing '{clip.name}' ({clip.length:F2}s)");

        byte[] wav = WavUtility.FromAudioClip(clip);
        if (wav == null || wav.Length < 100)
        {
            Debug.LogError($"[Tester] WAV conversion failed for '{clip.name}'. Skipping.");
            _playing = false;
            yield break;
        }

        // Play audio immediately through speaker
        _audioSource.clip = clip;
        _audioSource.Play();

        // Send to server and apply expression
        yield return StartCoroutine(SendAndApply(wav, clip.name));

        // Wait for audio playback to complete
        yield return new WaitWhile(() => _audioSource.isPlaying);

        // Fade to neutral after clip ends
        if (emotionController != null)
            emotionController.SetEmotion("neutral", 0.5f);

        _playing = false;
        Debug.Log($"[Tester] ✓ Finished '{clip.name}'");
    }

    // ── HTTP + Blendshape ─────────────────────────────────────────────────────

    IEnumerator SendAndApply(byte[] wav, string clipName)
    {
        WWWForm form = new WWWForm();
        form.AddBinaryData("file", wav, "audio.wav", "audio/wav");

        using UnityWebRequest req = UnityWebRequest.Post(serverUrl, form);
        req.timeout = timeoutSeconds;

        float t0 = Time.realtimeSinceStartup;
        yield return req.SendWebRequest();
        float elapsed = Time.realtimeSinceStartup - t0;

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[Tester] HTTP error ({elapsed:F2}s): {req.error}");
            yield break;
        }

        string json = req.downloadHandler.text;
        Debug.Log($"[Tester] Server response ({elapsed:F2}s): {json}");

        VoiceClipEmotionResponse r = JsonUtility.FromJson<VoiceClipEmotionResponse>(json);
        if (r == null || string.IsNullOrEmpty(r.emotion))
        {
            Debug.LogWarning($"[Tester] Parse failed for: {json}");
            yield break;
        }

        Debug.Log($"[Tester] APPLYING → emotion={r.emotion} conf={r.confidence:F2} transcript=\"{r.transcript}\"");

        if (emotionController != null)
        {
            emotionController.SetEmotion(r.emotion, r.confidence);
        }
        else
        {
            Debug.LogError("[Tester] emotionController is null — cannot apply expression!");
        }
    }

    public void OnPlayButtonPressed()
    {
        if (!_playing) StartCoroutine(PlayNextClip());
    }
}

// ── Response model ───────────────────────────────────────────────────────────
[System.Serializable]
public class VoiceClipEmotionResponse
{
    public string emotion;
    public float  confidence;
    public string transcript;
    public string source;
    public string request_id;
}
