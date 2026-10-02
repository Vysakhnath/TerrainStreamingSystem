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
                    Check(controller != null && controller.enabled && Active.Count == 1 && Active.ContainsKey(Vector2Int.zero), "Initial chunk without Start ordering dependency");
                    var mesh = Active[Vector2Int.zero].GetComponent<MeshFilter>().sharedMesh;
                    Check(mesh.vertexCount == 121 && mesh.triangles.Length == 600 && Mathf.Approximately(mesh.bounds.size.x, 20), "V0 mesh dimensions and topology preserved");
                    Check((Vector2Int)controller.GetType().GetMethod("GetChunkFromCoord", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(controller, new object[] { new Vector3(-0.1f, 0, -20.1f) }) == new Vector2Int(-1, -2), "Negative coordinates use floor indexing");
                    drone.position = new Vector3(20, 0, 0); Wait(1); break;
                case 1:
                    Check(Active.Count == 2 && Active.ContainsKey(new Vector2Int(1, 0)), "Boundary crossing requests one tile and retains previous tile");
                    var left = Active[Vector2Int.zero].GetComponent<MeshFilter>().sharedMesh.vertices;
                    var right = Active[new Vector2Int(1, 0)].GetComponent<MeshFilter>().sharedMesh.vertices;
                    for (int z = 0; z <= 10; z++) Check(Mathf.Approximately(left[z * 11 + 10].y, right[z * 11].y), "Shared edge height " + z);
                    drone.position = new Vector3(100, 0, 0); Wait(2); break;
                case 2:
                    Check(Active.Count == 1 && Active.ContainsKey(new Vector2Int(5, 0)), "Teleport releases distant tiles");
                    expectFailure = true; Set(generator, "resolution", 0);
                    drone.position = new Vector3(120, 0, 0); Wait(3); break;
                case 3:
                    Check(expectedFailures == 1 && !(bool)Get(controller, "isUpdating") && !Active.ContainsKey(new Vector2Int(6, 0)), "Failed generation releases guard and does not register chunk");
                    Check(((System.Collections.ICollection)Get(pool, "poolOfChunks")).Count == 2, "Failed generation returns acquired object");
                    expectFailure = false; Set(generator, "resolution", 10);
                    drone.position = new Vector3(140, 0, 0); Wait(4); break;
                case 4:
                    Check(Active.ContainsKey(new Vector2Int(7, 0)), "Streaming continues after generation failure");
                    var resetList = new List<Mesh>();
                    var pooled = (HashSet<GameObject>)Get(pool, "pooledChunks");
                    foreach (var item in Owned) if (pooled.Contains(item.Key)) resetList.Add(item.Value);
                    resetMeshes = resetList.ToArray();
                    pool.Reset(); Wait(5); break;
                case 5:
                    foreach (var reset in resetMeshes) Check(reset == null, "Pool reset destroys the released runtime mesh");
                    Check(Owned.Count == Active.Count && ((System.Collections.ICollection)Get(pool, "poolOfChunks")).Count == 0, "Pool reset disposes inactive objects and preserves active ownership");
                    var temporary = pool.GetChunkFromPool();
                    var temporaryMesh = temporary.Item2.GetComponent<MeshFilter>().sharedMesh;
                    generator.GenerateMeshData(new Vector2Int(-2, 1), 21.5f, temporary.Item2);
                    Check(Mathf.Approximately(temporaryMesh.bounds.size.x, 21.5f), "Non-divisible chunk size spans full tile");
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
                    Check(oldProvider == null && PerlinNoiseHeightProvider.GetInstance() != null && Active.Count == 1, "Scene reload initializes a fresh provider and terrain");
                    foreach (var old in oldMeshes) Check(old == null, "Owned runtime mesh released on scene unload");
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



