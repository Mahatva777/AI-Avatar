using UnityEngine;

public class EmotionControllerAdapter : MonoBehaviour
{
    public EmotionController emotionController;

    [Header("Stability Filter")]
    [Tooltip("Seconds an emotion must persist before it's accepted")]
    public float switchThreshold = 0.4f;
    [Tooltip("Minimum confidence (0..1) to accept an emotion")]
    public float minConfidence = 0.35f;

    [Header("Source Weighting")]
    [Tooltip("Multiplier applied to CNN confidence (fast but less contextual)")]
    public float cnnWeight = 0.85f;
    [Tooltip("Multiplier applied to LLM confidence (slow but contextually richer)")]
    public float llmWeight = 1.0f;

    private string _pendingEmotion   = "neutral";
    private float  _pendingStartTime = -1f;
    private string _confirmedEmotion = "neutral";

    /// <summary>
    /// Called by AudioSender with emotion data from /analyze (source="cnn")
    /// or from the SSE stream (source="llm").
    /// </summary>
    public void ReceiveEmotion(string emotion, float confidence, string source = "cnn")
    {
        // Apply source weighting
        float weight = source == "llm" ? llmWeight : cnnWeight;
        confidence *= weight;
        confidence  = Mathf.Clamp01(confidence);

        Debug.Log($"[Adapter] source={source}  emotion={emotion}  conf={confidence:F2}");

        // Confidence gate
        if (confidence < minConfidence)
        {
            emotion    = "neutral";
            confidence = 0.5f;
        }

        // ── Hysteresis: track how long this emotion has been pending ──────────
        // Bug fix: was using Time.deltaTime (single-frame delta, effectively broken).
        // Now uses Time.time timestamps for correct elapsed-time measurement.
        if (emotion != _pendingEmotion)
        {
            _pendingEmotion   = emotion;
            _pendingStartTime = Time.time;
        }

        float elapsed = (_pendingStartTime < 0f) ? switchThreshold : (Time.time - _pendingStartTime);

        // Confirm if persisted long enough, or immediately accept neutral/LLM
        bool instantAccept = (emotion == "neutral") || (source == "llm" && confidence > 0.7f);
        if (elapsed >= switchThreshold || instantAccept)
        {
            _confirmedEmotion = _pendingEmotion;
        }

        if (emotionController != null)
            emotionController.SetEmotion(_confirmedEmotion, confidence);
    }
}
