using UnityEngine;

public class PerlinNoiseHeightProvider : MonoBehaviour
{
    private static PerlinNoiseHeightProvider instance;

    private float scale = 0.05f;
    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Debug.LogWarning("Only one terrain height provider is supported per scene.", this);
            Destroy(this);
            return;
        }
        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    public static PerlinNoiseHeightProvider GetInstance()
    {
        return instance;
    }

    public float GetHeilghtForTerrain(Vector3 worldPos, float heightMultiplier)
    {
        var height = Mathf.PerlinNoise(worldPos.x * scale, worldPos.z * scale);
        return heightMultiplier * height;
    }
}
