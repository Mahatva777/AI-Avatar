using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

[RequireComponent(typeof(MicrophoneRecorder))]
public class AudioSender : MonoBehaviour
{
    [Header("References")]
    public MicrophoneRecorder recorder;
    public EmotionControllerAdapter emotionAdapter;

    [Header("Server")]
    public string serverUrl   = "http://127.0.0.1:8000/analyze";
    public float  sendInterval = 1.0f;   // poll every 1s (clip is 2s, no point faster)

    private AudioClip _lastSent  = null;
    private int       _pollCount = 0;
    private bool      _sending   = false;

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
    }

    IEnumerator PollLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(sendInterval);

            _pollCount++;
            AudioClip clip = recorder.ReadyClip;

            bool recording = recorder.IsRecording;
            bool hasClip   = clip != null;
            bool isNew     = clip != _lastSent;

            Debug.Log($"[Sender] Poll #{_pollCount}: recording={recording} hasClip={hasClip} isNew={isNew} sending={_sending}");

            if (!hasClip || !isNew || _sending) continue;

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

    IEnumerator PostAudio(byte[] wav)
    {
        WWWForm form = new WWWForm();
        form.AddBinaryData("file", wav, "audio.wav", "audio/wav");

        using UnityWebRequest req = UnityWebRequest.Post(serverUrl, form);
        req.timeout = 20;

        float t0 = Time.realtimeSinceStartup;
        yield return req.SendWebRequest();
        float elapsed = Time.realtimeSinceStartup - t0;

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[Sender] HTTP error ({elapsed:F1}s): {req.error}");
            yield break;
        }

        string json = req.downloadHandler.text;
        Debug.Log($"[Sender] Response ({elapsed:F1}s): {json}");

        EmotionResponse r = JsonUtility.FromJson<EmotionResponse>(json);
        if (r == null)
        {
            Debug.LogWarning("[Sender] JSON parse failed.");
            yield break;
        }

        Debug.Log($"[Sender] => emotion={r.emotion}  conf={r.confidence:F2}  transcript=\"{r.transcript}\"");

        if (emotionAdapter != null)
            emotionAdapter.ReceiveEmotion(r.emotion, r.confidence);
    }
}

[System.Serializable]
public class EmotionResponse
{
    public string emotion;
    public float  confidence;
    public string transcript;
}
