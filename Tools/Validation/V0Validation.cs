using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class V0Validation
{
    const string ScenePath = "Assets/Scenes/MainScene.unity";
    static int stage, targetFrame, expectedFailures;
    static double deadline;
    static bool running, expectFailure;
    static Exception pendingFailure;
    static ChunkController controller;
    static ChunksPoolManager pool;
    static TerrainGenerator generator;
    static Transform drone;
    static PerlinNoiseHeightProvider oldProvider;
    static Mesh[] oldMeshes;
    static Mesh[] resetMeshes;
    static int ownedBeforeFailure, pooledBeforeFailure, traversalStep;
    static readonly List<string> passed = new List<string>();
    static V0Validation()
    {
        if (SessionState.GetBool("V0ValidationRunning", false)) Attach();
    }
    public static void Run()
    {
        try
        {
            var build = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { ScenePath }, locationPathName = "Builds/Validation/DroneTerrainSystem.exe",
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.Development
            });
            Check(build.summary.result == BuildResult.Succeeded, "Windows development player build");
            File.WriteAllLines("build-checks.txt", passed);
            RunRuntime();
        }
        catch (Exception ex) { Fail(ex); }
    }
    public static void RunRuntime()
    {
        try
        {
            if (File.Exists("validation-failure.txt")) File.Delete("validation-failure.txt");
            if (File.Exists("runtime-checks.txt")) File.Delete("runtime-checks.txt");
            SessionState.SetBool("V0ValidationRunning", true);
            EditorSceneManager.OpenScene(ScenePath);
            Attach();
            EditorApplication.isPlaying = true;
        }
        catch (Exception ex) { Fail(ex); }
    }
    static void Attach()
    {
        running = true;
        deadline = EditorApplication.timeSinceStartup + 180;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= Log;
        Application.logMessageReceived += Log;
    }
    static object Get(object obj, string name) => obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj);
    static void Set(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(obj, value);
    static Dictionary<Vector2Int, GameObject> Active => (Dictionary<Vector2Int, GameObject>)Get(controller, "activeChunkDict");
    static Dictionary<GameObject, Mesh> Owned => (Dictionary<GameObject, Mesh>)Get(pool, "ownedChunks");
    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        passed.Add(label);
    }
    static void Wait(int next) { stage = next; targetFrame = Time.frameCount + 3; }
    static void Log(string message, string trace, LogType type)
    {
        if (!running || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
        // Unity 6.3 batch mode can fail to find its Editor Search database on startup.
        // Record this exact editor-only error separately; terrain errors still fail the run.
        if (message.StartsWith("ArgumentOutOfRangeException: Index was out of range") && trace.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup"))
        {
            File.AppendAllText("editor-search-warning.txt", message + "\n" + trace + "\n");
            return;
        }
        if (expectFailure && message.Contains("ArgumentOutOfRangeException") && message.Contains("Chunk size and mesh resolution must be positive and finite") && trace.Contains("TerrainGenerator.GenerateMeshData")) { expectedFailures++; return; }
        pendingFailure = new Exception("Unexpected Unity log: " + message + "\n" + trace);
    }
    static Vector2Int Center => new Vector2Int(Mathf.FloorToInt(drone.position.x / 20), Mathf.FloorToInt(drone.position.z / 20));
    static int PooledCount => ((System.Collections.ICollection)Get(pool, "poolOfChunks")).Count;
    static void CheckCoverage(string label)
    {
        int buffer = (int)Get(controller, "chunkBufferCount");
        int retention = buffer + (int)Get(controller, "chunkRetentionMargin");
        var center = Center;
        for (int x = -buffer; x <= buffer; x++)
            for (int z = -buffer; z <= buffer; z++)
                if (!Active.TryGetValue(center + new Vector2Int(x, z), out var chunk) || !chunk.activeSelf)
                    throw new Exception(label + ": missing requested tile " + (center + new Vector2Int(x, z)));
        foreach (var item in Active)
        {
            var offset = item.Key - center;
            if (Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y)) > retention)
                throw new Exception(label + ": tile outside retention square");
        }
        Check(Active.Count <= (2 * retention + 1) * (2 * retention + 1), label + ": coverage and active-count bound");
        Check(Owned.Count == Active.Count + PooledCount, label + ": every owned object is active or pooled");
    }
    static bool AllDestroyed(Mesh[] meshes)
    {
        foreach (var mesh in meshes) if (mesh != null) return false;
        return true;
    }
    static void CheckMeshReuse(GameObject temporary)
    {
        var publishedMesh = Active[Center].GetComponent<MeshFilter>().sharedMesh;
        var publishedVertices = publishedMesh.vertices;
        var publishedTriangles = publishedMesh.triangles;
        var publishedNormals = publishedMesh.normals;
        var publishedBounds = publishedMesh.bounds;
        var scratchMesh = temporary.GetComponent<MeshFilter>().sharedMesh;

        generator.GenerateMeshData(Center, 20, temporary);
        var regenerated = scratchMesh.vertices;
        bool matches = regenerated.Length == publishedVertices.Length;
        for (int i = 0; matches && i < regenerated.Length; i++) matches &= regenerated[i] == publishedVertices[i];
        Check(matches, "Regeneration preserves previously published vertex positions");

        Set(generator, "resolution", 4);
        generator.GenerateMeshData(new Vector2Int(17, -9), 21.5f, temporary);
        Check(scratchMesh.vertexCount == 25 && scratchMesh.triangles.Length == 96 && Mathf.Approximately(scratchMesh.bounds.size.x, 21.5f), "Resolution change rebuilds cached topology on an existing mesh");
        Set(generator, "resolution", 10);
        generator.GenerateMeshData(Center, 20, temporary);
        var restoredTriangles = scratchMesh.triangles;
        matches = restoredTriangles.Length == publishedTriangles.Length;
        for (int i = 0; matches && i < restoredTriangles.Length; i++) matches &= restoredTriangles[i] == publishedTriangles[i];
        Check(scratchMesh.vertexCount == 121 && matches, "Restored resolution preserves triangle winding and indices");

        var verticesAfter = publishedMesh.vertices;
        var trianglesAfter = publishedMesh.triangles;
        var normalsAfter = publishedMesh.normals;
        matches = publishedMesh.bounds == publishedBounds;
        for (int i = 0; matches && i < publishedVertices.Length; i++) matches &= verticesAfter[i] == publishedVertices[i] && normalsAfter[i] == publishedNormals[i];
        for (int i = 0; matches && i < publishedTriangles.Length; i++) matches &= trianglesAfter[i] == publishedTriangles[i];
        Check(matches, "Overwriting shared scratch buffers leaves published meshes unchanged");
    }
    static void Tick()
    {
        if (!running) return;
        if (pendingFailure != null) { Fail(pendingFailure); return; }
        if (EditorApplication.timeSinceStartup > deadline) { Fail(new Exception("Runtime validation timed out")); return; }
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling || Time.frameCount < targetFrame) return;
        try
        {
            switch (stage)
            {
                case 0:
                    if (Time.frameCount < 3) return;
                    controller = UnityEngine.Object.FindFirstObjectByType<ChunkController>();
                    pool = UnityEngine.Object.FindFirstObjectByType<ChunksPoolManager>();
                    generator = UnityEngine.Object.FindFirstObjectByType<TerrainGenerator>();
                    drone = GameObject.Find("Drone").transform;
                    Check(controller != null && controller.enabled && Active.Count == 25, "Default buffer 2 creates 25 tiles at startup");
                    CheckCoverage("Startup");
                    var mesh = Active[Vector2Int.zero].GetComponent<MeshFilter>().sharedMesh;
                    Check(mesh.vertexCount == 121 && mesh.triangles.Length == 600 && Mathf.Approximately(mesh.bounds.size.x, 20), "V0 mesh dimensions and topology preserved");
                    Check((Vector2Int)controller.GetType().GetMethod("GetChunkFromCoord", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, new object[] { new Vector3(-0.1f, 0, -20.1f) }) == new Vector2Int(-1, -2), "Negative coordinates use floor indexing");
                    drone.position = new Vector3(20, 0, 20); Wait(1); break;
                case 1:
                    CheckCoverage("Diagonal boundary crossing");
                    Check(Active.Count == 34, "One-ring margin retains visited tiles after diagonal movement");
                    var left = Active[Vector2Int.zero].GetComponent<MeshFilter>().sharedMesh.vertices;
                    var right = Active[new Vector2Int(1, 0)].GetComponent<MeshFilter>().sharedMesh.vertices;
                    for (int z = 0; z <= 10; z++) Check(Mathf.Approximately(left[z * 11 + 10].y, right[z * 11].y), "Shared edge height " + z);
                    ownedBeforeFailure = Owned.Count;
                    drone.position = new Vector3(1000, 0, -1000); Wait(2); break;
                case 2:
                    CheckCoverage("Distant negative-Z teleport");
                    Check(Active.Count == 25 && Owned.Count == ownedBeforeFailure, "Teleport releases before acquisition and reuses existing objects");
                    pooledBeforeFailure = PooledCount;
                    expectFailure = true; Set(generator, "resolution", 0);
                    drone.position = new Vector3(1020, 0, -1000); Wait(3); break;
                case 3:
                    Check(expectedFailures == 1 && !(bool)Get(controller, "isUpdating") && Active.Count == 25, "Failed generation releases guard and does not register a tile");
                    Check(PooledCount == pooledBeforeFailure && Owned.Count == ownedBeforeFailure, "Failed generation returns the acquired object");
                    expectFailure = false; Set(generator, "resolution", 10);
                    drone.position = new Vector3(1040, 0, -1000); Wait(4); break;
                case 4:
                    CheckCoverage("Recovery on next boundary");
                    var resetList = new List<Mesh>();
                    var pooled = (HashSet<GameObject>)Get(pool, "pooledChunks");
                    foreach (var item in Owned) if (pooled.Contains(item.Key)) resetList.Add(item.Value);
                    resetMeshes = resetList.ToArray();
                    Check(resetMeshes.Length > 0, "Reset test has available meshes to release");
                    pool.Reset(); Wait(5); break;
                case 5:
                    Check(AllDestroyed(resetMeshes), "Pool reset destroys released runtime meshes");
                    Check(Owned.Count == Active.Count && PooledCount == 0, "Pool reset preserves active ownership");
                    var temporary = pool.GetChunkFromPool();
                    var temporaryMesh = temporary.Item2.GetComponent<MeshFilter>().sharedMesh;
                    generator.GenerateMeshData(new Vector2Int(-2, 1), 21.5f, temporary.Item2);
                    Check(Mathf.Approximately(temporaryMesh.bounds.size.x, 21.5f), "Non-divisible chunk size spans full tile");
                    CheckMeshReuse(temporary.Item2);
                    pool.SetPool(temporary);
                    bool rejected = false;
                    try { pool.SetPool(temporary); } catch (InvalidOperationException) { rejected = true; }
                    Check(rejected, "Duplicate pool return rejected");
                    oldProvider = PerlinNoiseHeightProvider.GetInstance();
                    oldMeshes = new List<Mesh>(Owned.Values).ToArray();
                    SceneManager.LoadScene("MainScene"); Wait(6); break;
                case 6:
                    controller = UnityEngine.Object.FindFirstObjectByType<ChunkController>();
                    pool = UnityEngine.Object.FindFirstObjectByType<ChunksPoolManager>();
                    drone = GameObject.Find("Drone").transform;
                    Check(oldProvider == null && PerlinNoiseHeightProvider.GetInstance() != null && Active.Count == 25, "Scene reload initializes provider and configured neighborhood");
                    Check(AllDestroyed(oldMeshes), "Owned meshes released on scene unload");
                    Set(controller, "chunkBufferCount", 3); Wait(7); break;
                case 7:
                    CheckCoverage("Buffer 3 changed while stationary");
                    Check(Active.Count == 49 && Active.ContainsKey(new Vector2Int(3, 3)) && Active.ContainsKey(new Vector2Int(-3, -3)), "7 by 7 neighborhood retains its corners");
                    Set(controller, "chunkBufferCount", 1); Set(controller, "chunkRetentionMargin", 0); Wait(8); break;
                case 8:
                    CheckCoverage("Buffer 1 and zero margin while stationary");
                    Check(Active.Count == 9 && Owned.Count == 49, "Shrinking returns excess tiles without destroying reusable objects");
                    Set(controller, "chunkBufferCount", 2); Set(controller, "chunkRetentionMargin", 1); Wait(9); break;
                case 9:
                    CheckCoverage("Buffer 2 restored while stationary");
                    if (traversalStep < 24)
                    {
                        drone.position = traversalStep % 5 == 0
                            ? new Vector3(-2000 - traversalStep * 20, 0, 2000 + traversalStep * 20)
                            : new Vector3(traversalStep * 20, 0, (traversalStep % 4) * 20);
                        traversalStep++; Wait(10);
                    }
                    else { Set(controller, "chunkBufferCount", 0); Set(controller, "chunkRetentionMargin", 0); Wait(11); }
                    break;
                case 10:
                    CheckCoverage("Traversal " + traversalStep);
                    Check(Owned.Count == 49, "Traversal " + traversalStep + ": pool high-water count remains 49");
                    Wait(9); break;
                case 11:
                    CheckCoverage("Buffer zero");
                    Check(Active.Count == 1, "Buffer zero still supports a single requested tile");
                    Set(controller, "chunkBufferCount", -1); Set(controller, "chunkRetentionMargin", -1);
                    controller.GetType().GetMethod("OnValidate", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, null);
                    Check((int)Get(controller, "chunkBufferCount") == 0 && (int)Get(controller, "chunkRetentionMargin") == 0, "Inspector validation clamps negative radii");
                    File.WriteAllLines("runtime-checks.txt", passed);
                    running = false; SessionState.SetBool("V0ValidationRunning", false);
                    EditorApplication.isPlaying = false;
                    EditorApplication.Exit(0); break;
            }
        }
        catch (Exception ex) { Fail(ex); }
    }
    static void Fail(Exception ex)
    {
        running = false;
        SessionState.SetBool("V0ValidationRunning", false);
        File.WriteAllText("validation-failure.txt", ex.ToString());
        EditorApplication.Exit(1);
    }
}
