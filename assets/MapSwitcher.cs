using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class MapSwitcher : MonoBehaviour
{
    public static bool IsMenuOpen { get; private set; }

    public Key toggleKey = Key.M;

    private const int WindowId = 837450;
    private Rect windowRect = new Rect(Screen.width / 2f - 160f, Screen.height / 2f - 120f, 320f, 240f);
    private int activeMapIndex = -1;
    private readonly List<MapEntry> maps = new();
    private bool initialized;

    [Serializable]
    private class MapEntry
    {
        public string displayName;
        public string[] rootObjectNames;
        public Vector3 spawnPoint;
        public string prefabPath;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (FindAnyObjectByType<MapSwitcher>() != null)
        {
            return;
        }

        var go = new GameObject("MapSwitcher");
        DontDestroyOnLoad(go);
        go.AddComponent<MapSwitcher>();
    }

    private void Start()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (initialized)
        {
            return;
        }

        maps.Clear();

        maps.Add(new MapEntry
        {
            displayName = "Original Map",
            rootObjectNames = new[] { "MapBuilder", "Plane" },
            spawnPoint = new Vector3(0f, 1f, 0f)
        });

        maps.Add(new MapEntry
        {
            displayName = "Japanese Restaurant Inakaya",
            rootObjectNames = new[] { "Japanese_Restaurant_Map" },
            spawnPoint = new Vector3(0f, 0.5f, 0f),
            prefabPath = "Assets/maps/Japanese Restaurant Inakaya/source/Inakaya_Cycles2.fbx"
        });

        DetectActiveMap();
        initialized = true;
    }

    private void DetectActiveMap()
    {
        for (var i = 0; i < maps.Count; i++)
        {
            var allActive = true;
            foreach (var objName in maps[i].rootObjectNames)
            {
                var go = FindIncludingInactive(objName);
                if (go == null || !go.activeSelf)
                {
                    allActive = false;
                    break;
                }
            }

            if (allActive)
            {
                activeMapIndex = i;
            }
        }

        if (activeMapIndex < 0 && maps.Count > 0)
        {
            activeMapIndex = 0;
        }
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (CharacterTuningMenu.IsOpen)
        {
            if (IsMenuOpen)
            {
                IsMenuOpen = false;
            }
            return;
        }

        if (keyboard[toggleKey].wasPressedThisFrame)
        {
            if (!initialized)
            {
                Initialize();
            }

            IsMenuOpen = !IsMenuOpen;
            if (IsMenuOpen)
            {
                windowRect = new Rect(Screen.width / 2f - 160f, Screen.height / 2f - 120f, 320f, 0f);
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        if (IsMenuOpen && keyboard.escapeKey.wasPressedThisFrame)
        {
            IsMenuOpen = false;
        }
    }

    private void OnGUI()
    {
        if (!IsMenuOpen)
        {
            return;
        }

        GUI.depth = -100;
        windowRect = GUILayout.Window(WindowId, windowRect, DrawWindow, "Select Map  (M)");
    }

    private void DrawWindow(int id)
    {
        GUILayout.Space(8f);

        if (maps.Count == 0)
        {
            GUILayout.Label("No maps found on the scene.");
            GUILayout.Space(8f);
            if (GUILayout.Button("Close", GUILayout.Height(30f)))
            {
                IsMenuOpen = false;
            }
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
            return;
        }

        for (var i = 0; i < maps.Count; i++)
        {
            var map = maps[i];
            var isCurrent = i == activeMapIndex;

            var oldColor = GUI.color;
            if (isCurrent)
            {
                GUI.color = new Color(0.55f, 0.95f, 0.55f, 1f);
            }

            GUILayout.BeginHorizontal("box");
            GUILayout.Label(isCurrent ? "►" : " ", GUILayout.Width(18f));
            GUILayout.Label(map.displayName);

            GUI.enabled = !isCurrent;
            if (GUILayout.Button(isCurrent ? "Active" : "Switch", GUILayout.Width(70f), GUILayout.Height(28f)))
            {
                SwitchToMap(i);
            }
            GUI.enabled = true;

            GUILayout.EndHorizontal();
            GUI.color = oldColor;
        }

        GUILayout.Space(10f);

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Close", GUILayout.Width(80f), GUILayout.Height(28f)))
        {
            IsMenuOpen = false;
        }
        GUILayout.EndHorizontal();

        GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
    }

    private void SwitchToMap(int index)
    {
        if (index < 0 || index >= maps.Count)
        {
            return;
        }

        // Ensure the new map is instantiated before we switch
        var selected = maps[index];
        EnsureMapInstantiated(selected);

        // Disable ALL map roots
        for (var i = 0; i < maps.Count; i++)
        {
            SetMapActive(maps[i], false);
        }

        // Enable the selected map
        SetMapActive(selected, true);
        activeMapIndex = index;

        // Teleport all player characters to the spawn point
        TeleportPlayers(selected.spawnPoint);

        IsMenuOpen = false;
        Debug.Log($"[MapSwitcher] Switched to: {selected.displayName}");
    }

    private void EnsureMapInstantiated(MapEntry map)
    {
        var go = FindIncludingInactive(map.rootObjectNames[0]);
        if (go != null) return;

        if (!string.IsNullOrEmpty(map.prefabPath))
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(map.prefabPath);
            if (prefab != null)
            {
                go = Instantiate(prefab);
                go.name = map.rootObjectNames[0];
                
                // Auto-add colliders if it's a static map
                foreach (var filter in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.gameObject.GetComponent<Collider>() == null)
                    {
                        var collider = filter.gameObject.AddComponent<MeshCollider>();
                        // Convex is not needed for static environments, but keep it robust
                    }
                }
                
                // Move it slightly down if it floats, or just leave at origin
                go.transform.position = Vector3.zero;
            }
            else
            {
                Debug.LogError($"[MapSwitcher] Failed to load prefab at {map.prefabPath}");
            }
#endif
        }
    }

    private void SetMapActive(MapEntry map, bool active)
    {
        foreach (var objName in map.rootObjectNames)
        {
            var go = FindIncludingInactive(objName);
            if (go != null)
            {
                go.SetActive(active);
            }
        }
    }

    private void TeleportPlayers(Vector3 spawnPoint)
    {
        var players = FindObjectsByType<PlayerMove>(FindObjectsInactive.Include);
        foreach (var player in players)
        {
            // Temporarily disable CharacterController so we can set position directly
            var cc = player.GetComponent<CharacterController>();
            if (cc != null)
            {
                cc.enabled = false;
            }

            player.transform.position = spawnPoint;

            if (cc != null)
            {
                cc.enabled = player.enabled;
            }
        }

        // Also snap camera
        var cameraFollow = Camera.main != null ? Camera.main.GetComponent<CameraFollow>() : null;
        if (cameraFollow != null)
        {
            cameraFollow.SnapToTarget();
        }
    }

    /// <summary>
    /// Finds a root GameObject by name, even if it is inactive.
    /// </summary>
    private static GameObject FindIncludingInactive(string objectName)
    {
        // First try the fast path
        var found = GameObject.Find(objectName);
        if (found != null)
        {
            return found;
        }

        // Search all root objects including inactive ones
        foreach (var root in GetAllRootGameObjects())
        {
            if (root.name == objectName)
            {
                return root;
            }
        }

        return null;
    }

    private static GameObject[] GetAllRootGameObjects()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        return scene.GetRootGameObjects();
    }
}
