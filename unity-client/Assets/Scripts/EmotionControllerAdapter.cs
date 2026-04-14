using UnityEngine;

public class EmotionControllerAdapter : MonoBehaviour
{
    public EmotionController emotionController;

    private string currentEmotion = "neutral";
    private float currentWeight = 0f;
    private float smoothSpeed = 3f;
    private float emotionTimer = 0f;
    private float switchThreshold = 0.5f;

    public void ReceiveEmotion(string emotion, float confidence)
    {
     
        if (emotion == currentEmotion)
        {
            emotionTimer += Time.deltaTime;
        }
        else
        {
            emotionTimer = 0f;
        }

        if (emotionTimer > switchThreshold)
        {
            currentEmotion = emotion;
        }

        float intensity = confidence * 100f;

        currentWeight = Mathf.Lerp(currentWeight, intensity, Time.deltaTime * smoothSpeed);

        if (emotionController != null)
        {
            emotionController.SetEmotion(currentEmotion, currentWeight);
        }
    }
}