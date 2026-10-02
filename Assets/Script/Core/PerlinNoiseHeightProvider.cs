using Unity.VisualScripting;
using UnityEngine;

public class PerlinNoiseHeightProvider : MonoBehaviour
{
    private static PerlinNoiseHeightProvider instance;

    private float scale = 0.05f;
    private void Awake()
    {
        if (instance != null)
        {
            Destroy(instance);
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
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
