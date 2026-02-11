using UnityEngine;

public class ApplyMaterialToChildren : MonoBehaviour
{
    public Material material;

    [ContextMenu("Apply")]
    public void Apply()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();

        foreach (Renderer r in renderers)
        {
            r.sharedMaterial = material;
        }
    }
}
