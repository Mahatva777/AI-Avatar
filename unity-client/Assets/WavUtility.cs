using System;
using System.IO;
using UnityEngine;

/// Converts Unity AudioClip -> standard 16-bit PCM WAV bytes.
public static class WavUtility
{
    const int HEADER_SIZE = 44;

    public static byte[] FromAudioClip(AudioClip clip)
    {
        if (clip == null)
        {
            Debug.LogError("[WavUtility] clip is null.");
            return null;
        }

        int   samples  = clip.samples;
        int   channels = clip.channels;
        int   rate     = clip.frequency;

        float[] pcmFloat = new float[samples * channels];
        clip.GetData(pcmFloat, 0);

        // Convert float [-1,1] to 16-bit PCM
        short[] pcm16 = new short[pcmFloat.Length];
        for (int i = 0; i < pcmFloat.Length; i++)
        {
            float clamped = Mathf.Clamp(pcmFloat[i], -1f, 1f);
            pcm16[i] = (short)(clamped * short.MaxValue);
        }

        byte[] pcmBytes = new byte[pcm16.Length * 2];
        Buffer.BlockCopy(pcm16, 0, pcmBytes, 0, pcmBytes.Length);

        using MemoryStream ms = new MemoryStream();
        using BinaryWriter bw = new BinaryWriter(ms);

        int byteRate    = rate * channels * 2;
        int dataLen     = pcmBytes.Length;
        int chunkSize   = 36 + dataLen;

        // RIFF header
        bw.Write(new char[] { 'R','I','F','F' });
        bw.Write(chunkSize);
        bw.Write(new char[] { 'W','A','V','E' });

        // fmt sub-chunk
        bw.Write(new char[] { 'f','m','t',' ' });
        bw.Write(16);            // sub-chunk size
        bw.Write((short)1);      // PCM = 1
        bw.Write((short)channels);
        bw.Write(rate);
        bw.Write(byteRate);
        bw.Write((short)(channels * 2));  // block align
        bw.Write((short)16);              // bits per sample

        // data sub-chunk
        bw.Write(new char[] { 'd','a','t','a' });
        bw.Write(dataLen);
        bw.Write(pcmBytes);

        byte[] result = ms.ToArray();
        Debug.Log($"[WavUtility] Built WAV: {result.Length} bytes | {samples} samples | {channels}ch | {rate}Hz");
        return result;
    }
}
