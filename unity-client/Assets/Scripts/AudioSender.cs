using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;

public class AudioSender : MonoBehaviour
{
    public MicrophoneRecorder recorder;
    public string serverUrl = "http://127.0.0.1:8000/predict";
    public float sendInterval = 2f;

    void Start()
    {
        StartCoroutine(SendLoop());
    }

    IEnumerator SendLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(sendInterval);

            AudioClip clip = recorder.GetClip();
            if (clip == null) continue;

            byte[] wav = WavUtility.FromAudioClip(clip);
            yield return StartCoroutine(PostAudio(wav));
        }
    }

  IEnumerator PostAudio(byte[] wav)
{
    WWWForm form = new WWWForm();
    form.AddBinaryData("file", wav, "audio.wav", "audio/wav");

    using (UnityWebRequest www = UnityWebRequest.Post(serverUrl, form))
    {
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.Success)
        {
            string json = www.downloadHandler.text;

            EmotionResponse response = JsonUtility.FromJson<EmotionResponse>(json);

            EmotionControllerAdapter adapter =
                FindObjectOfType<EmotionControllerAdapter>();

            if (adapter != null)
            {
                adapter.ReceiveEmotion(response.emotion, response.confidence);
            }
        }
        else
        {
            Debug.LogError(www.error);
        }
    }
}

[System.Serializable]
public class EmotionResponse
{
    public string emotion;
    public float confidence;
}
}