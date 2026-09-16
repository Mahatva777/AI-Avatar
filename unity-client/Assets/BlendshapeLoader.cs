using UnityEngine;

public class BlendshapeLoader : MonoBehaviour
{
    public SkinnedMeshRenderer faceRenderer;

    void Start()
    {
        if (faceRenderer == null)
        {
            Debug.LogError("FaceRenderer not assigned!");
            return;
        }

        Mesh m = faceRenderer.sharedMesh;

        for (int i = 0; i < m.blendShapeCount; i++)
        {
            Debug.Log("Index: " + i + " Name: " + m.GetBlendShapeName(i));
        }
    }
}