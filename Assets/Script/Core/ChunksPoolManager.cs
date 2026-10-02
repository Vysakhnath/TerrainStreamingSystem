using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ChunksPoolManager : MonoBehaviour
{
    [SerializeField]
    private Material terrainMat;

    private Dictionary<Vector2Int, GameObject> poolOfChunks = new Dictionary<Vector2Int, GameObject>();

    public void SetPool((Vector2Int, GameObject) poolObject)
    {
        poolObject.Item2.SetActive(false);
        poolObject.Item2.transform.SetParent(transform);
        poolOfChunks.Add(poolObject.Item1, poolObject.Item2);
    }

    public (Vector2Int, GameObject)  GetChunkFromPool()
    {
        if (poolOfChunks.Count > 0)
        {
            var item = (poolOfChunks.Keys.First(), poolOfChunks.Values.First());
            poolOfChunks.Remove(item.Item1);
            return item;
        }
        else
        {
            var terrain = new GameObject();
            terrain.AddComponent<MeshFilter>();
            terrain.AddComponent<MeshRenderer>();
            terrain.GetComponent<MeshRenderer>().material = terrainMat;
            var item = (new Vector2Int(), terrain);
            return item;
        }
    }

    public void Reset()
    {
        poolOfChunks.Clear(); 
    }
}
