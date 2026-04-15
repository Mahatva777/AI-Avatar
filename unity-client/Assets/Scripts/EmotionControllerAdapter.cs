using UnityEngine;

public class EmotionControllerAdapter : MonoBehaviour
{
    public EmotionController emotionController;

    [Header("Stability Filter")]
    [Tooltip("Seconds an emotion must persist before it's accepted")]
    public float switchThreshold = 0.4f;
    [Tooltip("Minimum confidence (0..1) to accept an emotion")]
    public float minConfidence = 0.35f;

    private string pendingEmotion  = "neutral";
    private float  pendingTimer    = 0f;
    private string confirmedEmotion = "neutral";

    /// <summary>Called by AudioSender with data from /analyze endpoint.</summary>
    public void ReceiveEmotion(string emotion, float confidence)
    {
        Debug.Log($"[Adapter] emotion={emotion}  conf={confidence:F2}");

        if (confidence < minConfidence)
        {
            emotion = "neutral";
            confidence = 0.5f;
        }

        if (emotion == pendingEmotion)
        {
            pendingTimer += Time.deltaTime;
        }
        else
        {
            pendingEmotion = emotion;
            pendingTimer   = 0f;
        }

        if (pendingTimer >= switchThreshold || emotion == "neutral")
        {
            confirmedEmotion = pendingEmotion;
        }

        if (emotionController != null)
            emotionController.SetEmotion(confirmedEmotion, confidence);
    }
}
