using UnityEngine;

/// <summary>
/// EmotionController — VRoid blendshape driver
/// Supports: happy, sad, angry, neutral
/// Drives layered Fcl_ALL / Fcl_BRW / Fcl_EYE / Fcl_MTH blendshapes with smooth Lerp.
/// </summary>
[RequireComponent(typeof(SkinnedMeshRenderer))]
public class EmotionController : MonoBehaviour
{
    public SkinnedMeshRenderer faceRenderer;

    // Blendshape indices (filled in Start)
    int joyAll,     joyBrow,     joyEye,     joyMth;
    int angryAll,   angryBrow,   angryEye,   angryMth;
    int sadAll,     sadBrow,     sadEye,     sadMth;
    int neutralAll, neutralEye,  neutralMth;

    int[]   allIndices;
    float[] targetWeights;

    [Header("Boosting / Mapping")]
    public AnimationCurve emotionCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.3f, 0.6f),
        new Keyframe(0.6f, 0.9f),
        new Keyframe(1f, 1f)
    );
    [Range(0.2f, 2f)]  public float boostExponent   = 0.6f;
    [Range(1f, 2f)]    public float boostMultiplier  = 1.2f;

    [Header("Smoothing")]
    public float lerpSpeed = 8f;

    [Header("Neutral Fade")]
    [Tooltip("Speed at which face returns to neutral when emotion = neutral")]
    public float neutralFadeSpeed = 5f;

    [Header("Minimum Visibility")]
    [Range(0f, 1f)]   public float minConfidenceForMinimum = 0.3f;
    [Range(0f, 100f)] public float enforcedMinimumWeight   = 50f;

    string lastEmotion = "";

    void Start()
    {
        if (faceRenderer == null)
            faceRenderer = GetComponent<SkinnedMeshRenderer>();

        Mesh m = faceRenderer.sharedMesh;

        // Joy / Happy
        joyAll  = m.GetBlendShapeIndex("Fcl_ALL_Joy");
        joyBrow = m.GetBlendShapeIndex("Fcl_BRW_Joy");
        joyEye  = m.GetBlendShapeIndex("Fcl_EYE_Joy");
        joyMth  = m.GetBlendShapeIndex("Fcl_MTH_Joy");

        // Angry
        angryAll  = m.GetBlendShapeIndex("Fcl_ALL_Angry");
        angryBrow = m.GetBlendShapeIndex("Fcl_BRW_Angry");
        angryEye  = m.GetBlendShapeIndex("Fcl_EYE_Angry");
        angryMth  = m.GetBlendShapeIndex("Fcl_MTH_Angry");

        // Sad / Sorrow
        sadAll  = m.GetBlendShapeIndex("Fcl_ALL_Sorrow");
        sadBrow = m.GetBlendShapeIndex("Fcl_BRW_Sorrow");
        sadEye  = m.GetBlendShapeIndex("Fcl_EYE_Sorrow");
        sadMth  = m.GetBlendShapeIndex("Fcl_MTH_Sorrow");

        // Neutral  (Fcl_ALL_Neutral + Fcl_EYE_Natural + Fcl_MTH_Neutral)
        neutralAll = m.GetBlendShapeIndex("Fcl_ALL_Neutral");
        neutralEye = m.GetBlendShapeIndex("Fcl_EYE_Natural");   // your avatar uses "Natural"
        neutralMth = m.GetBlendShapeIndex("Fcl_MTH_Neutral");

        allIndices = new int[]
        {
            joyAll,     joyBrow,     joyEye,     joyMth,
            angryAll,   angryBrow,   angryEye,   angryMth,
            sadAll,     sadBrow,     sadEye,     sadMth,
            neutralAll, neutralEye,  neutralMth
        };

        targetWeights = new float[allIndices.Length];
        for (int i = 0; i < allIndices.Length; i++)
            targetWeights[i] = allIndices[i] == -1 ? 0f
                : faceRenderer.GetBlendShapeWeight(allIndices[i]);
    }

    /// <summary>
    /// Call with emotion = "happy" | "sad" | "angry" | "neutral"
    /// intensity: 0..1 preferred (also accepts legacy 0..100)
    /// </summary>
    public void SetEmotion(string emotion, float intensity = 1f)
    {
        // Normalise to 0..1
        float conf = intensity > 1f ? Mathf.Clamp01(intensity / 100f)
                                    : Mathf.Clamp01(intensity);

        // Boost
        float boosted = (emotionCurve != null && emotionCurve.length > 0)
            ? Mathf.Clamp01(emotionCurve.Evaluate(conf) * boostMultiplier)
            : Mathf.Clamp01(Mathf.Pow(conf, boostExponent) * boostMultiplier);

        float weight = boosted * 100f;
        if (conf > minConfidenceForMinimum)
            weight = Mathf.Max(weight, enforcedMinimumWeight);

        // Zero all targets first
        for (int i = 0; i < targetWeights.Length; i++)
            targetWeights[i] = 0f;

        switch (emotion)
        {
            case "happy":
                Set(joyAll, weight); Set(joyBrow, weight);
                Set(joyEye, weight); Set(joyMth,  weight);
                break;
            case "angry":
                Set(angryAll, weight); Set(angryBrow, weight);
                Set(angryEye, weight); Set(angryMth,  weight);
                break;
            case "sad":
                Set(sadAll, weight); Set(sadBrow, weight);
                Set(sadEye, weight); Set(sadMth,  weight);
                break;
            case "neutral":
            case "":
                // Neutral: gently raise Fcl_ALL_Neutral + natural eye, rest stay 0
                Set(neutralAll, weight * 0.5f);
                Set(neutralEye, weight * 0.3f);
                Set(neutralMth, weight * 0.2f);
                break;
            // unknown → stays zeroed → SoftReset behaviour
        }

        lastEmotion = emotion;
    }

    void Set(int blendIndex, float weight)
    {
        if (blendIndex == -1) return;
        for (int i = 0; i < allIndices.Length; i++)
        {
            if (allIndices[i] == blendIndex) { targetWeights[i] = weight; return; }
        }
    }

    void Update()
    {
        float speed = lastEmotion == "neutral" ? neutralFadeSpeed : lerpSpeed;
        for (int i = 0; i < allIndices.Length; i++)
        {
            int idx = allIndices[i];
            if (idx == -1) continue;
            float cur  = faceRenderer.GetBlendShapeWeight(idx);
            float next = Mathf.Lerp(cur, targetWeights[i], Time.deltaTime * speed);
            faceRenderer.SetBlendShapeWeight(idx, next);
        }
    }

    public void SoftResetFace()
    {
        for (int i = 0; i < targetWeights.Length; i++) targetWeights[i] = 0f;
        lastEmotion = "";
    }

    public void HardResetFace()
    {
        for (int i = 0; i < allIndices.Length; i++)
        {
            int idx = allIndices[i];
            if (idx != -1) faceRenderer.SetBlendShapeWeight(idx, 0f);
            targetWeights[i] = 0f;
        }
        lastEmotion = "";
    }
}
