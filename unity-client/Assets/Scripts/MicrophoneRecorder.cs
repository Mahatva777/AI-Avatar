
using UnityEngine;

public class MicrophoneRecorder : MonoBehaviour
{
    public int sampleRate = 16000;
    private AudioClip clip;
    private string mic;

    void Start()
    {
        mic = Microphone.devices[0];
        clip = Microphone.Start(mic, true, 1, sampleRate);
    }

    public AudioClip GetClip()
    {
        return clip;
    }
}
