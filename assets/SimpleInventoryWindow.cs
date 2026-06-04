using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class SimpleInventoryWindow : MonoBehaviour
{
    public static bool IsOpen { get; private set; }

    public Key toggleKey = Key.Q;
    public string title = "Studio Inventory";
    public float moveGroundY = 0f;
    public float minScale = 0.03f;
    public float maxScale = 30f;
    public float scaleStep = 0.1f;
    public float rotateStep = 15f;
    public float spawnScale = 1f;

    private const int WindowId = 837401;
    private const int TargetCatalogItems = 180;
    private const string HandheldPosePrefsKey = "StudioHandheldPoseOverridesV1";
    private const string KenneyModelPath = "Assets/DownloadedProps/KenneyFurniture/Models/";
    private const string KenneyTopdownPath = "Assets/DownloadedProps/KenneyFurniture/Topdown/";
    private static readonly string[] CuratedCreateItems =
    {
        "bedDouble",
        "bedSingle",
        "bedBunk",
        "bathtub",
        "shower",
        "toilet",
        "bathroomSink",
        "bathroomMirror",
        "bathroomCabinet",
        "loungeSofa",
        "loungeSofaLong",
        "loungeChair",
        "chair",
        "chairCushion",
        "chairDesk",
        "bench",
        "table",
        "tableRound",
        "tableCoffee",
        "tableGlass",
        "desk",
        "deskCorner",
        "sideTable",
        "sideTableDrawers",
        "bookcaseOpen",
        "bookcaseClosed",
        "books",
        "kitchenFridge",
        "kitchenFridgeSmall",
        "kitchenStove",
        "kitchenSink",
        "kitchenMicrowave",
        "kitchenCoffeeMachine",
        "kitchenCabinet",
        "lampRoundFloor",
        "lampSquareTable",
        "televisionModern",
        "pottedPlant",
        "computerScreen",
        "laptop",
        "bear",
        "chairRounded",
        "loungeChairRelax",
        "loungeSofaCorner",
        "loungeSofaOttoman",
        "stoolBar",
        "stoolBarSquare",
        "tableCoffeeSquare",
        "kitchenBlender",
        "radio",
        "televisionVintage",
        "computerKeyboard",
        "computerMouse",
        "cardboardBoxOpen",
        "cardboardBoxClosed",
        "plantSmall1",
        "plantSmall2",
        "plantSmall3",
        "rugRectangle",
        "rugRound",
        "washer",
        "dryer",
        "pillow",
        "pillowLong",
        "trashcan",
        "coatRackStanding"
    };
    private readonly List<Transform> movables = new();
    private readonly List<InventoryEntry> catalog = new();
    private readonly List<HandheldEntry> handheldCatalog = new();
    private readonly Dictionary<string, HandheldPose> handheldPoseOverrides = new();
    private readonly Dictionary<string, Texture2D> iconCache = new();
    private readonly Dictionary<string, Texture2D> handheldIconCache = new();
    private Rect windowRect = new Rect(24f, 72f, 680f, 720f);
    private Vector2 catalogScroll;
    private Vector2 sceneScroll;
    private Vector2 handsScroll;
    private Vector2 recsScroll;
    private Transform selected;
    private float selectedScale = 1f;
    private double nextRefreshTime;
    private int tab;
    private int spawnedCounter;
    private int categoryIndex = 0;
    private readonly string[] categories = { "All", "Seating", "Beds", "Tables", "Kitchen/Bath", "Office", "Decor" };
    private string searchQuery = "";

    private GUIStyle windowStyle;
    private GUIStyle buttonStyle;
    private GUIStyle activeButtonStyle;
    private GUIStyle smallButtonStyle;
    private GUIStyle tileStyle;
    private GUIStyle tileLabelStyle;
    private GUIStyle iconBoxStyle;
    private GUIStyle labelStyle;
    private GUIStyle mutedLabelStyle;
    private GUIStyle headerStyle;
    private GUIStyle tabStyle;
    private GUIStyle activeTabStyle;
    private GUIStyle panelStyle;
    private GUIStyle selectedPillStyle;

    private enum HandheldShape
    {
        Apple,
        GolfClub,
        Phone,
        Microphone,
        Bottle,
        Cup,
        Book,
        Flashlight,
        Tablet,
        Notebook,
        Keys,
        SodaCan,
        PizzaSlice,
        Donut,
        GameController,
        Headphones,
        Candle,
        PictureFrame,
        Bowl,
        Spoon,
        Toothbrush,
        Pen
    }

    private class InventoryEntry
    {
        public string label;
        public Transform template;
        public Texture2D icon;
    }

    private class HandheldEntry
    {
        public string label;
        public HandheldShape shape;
        public Color color;
        public Vector3 localPosition;
        public Vector3 localEuler;
        public Vector3 localScale;
    }

    [Serializable]
    private class HandheldPose
    {
        public string label;
        public Vector3 localPosition;
        public Vector3 localEuler;
        public Vector3 localScale;
    }

    [Serializable]
    private class HandheldPoseCollection
    {
        public HandheldPose[] items;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (FindAnyObjectByType<SimpleInventoryWindow>() != null)
        {
            return;
        }

        var go = new GameObject("SimpleInventoryWindow");
        DontDestroyOnLoad(go);
        go.AddComponent<SimpleInventoryWindow>();
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        if (SurfaceItemPlacer.BlocksToolInput || CharacterTuningMenu.IsOpen)
        {
            if (IsOpen)
            {
                SetOpen(false);
            }
            return;
        }

        if (keyboard[toggleKey].wasPressedThisFrame)
        {
            SetOpen(!IsOpen);
            RefreshAll();
        }

        if (!IsOpen)
        {
            return;
        }

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            SetOpen(false);
            return;
        }

        if (Time.unscaledTimeAsDouble >= nextRefreshTime)
        {
            RefreshAll();
        }

        HandleKeyboardShortcuts(keyboard);
        HandleMouseMoveAndPick();
    }

    private void OnDisable()
    {
        SetOpen(false);
    }

    private void OnGUI()
    {
        if (!IsOpen)
        {
            return;
        }

        InitStyles();
        GUI.depth = -50;
        ClampWindowToScreen();
        windowRect = GUI.Window(WindowId, windowRect, DrawWindow, title, windowStyle);
        ClampWindowToScreen();
    }

    private void Awake()
    {
        LoadHandheldPoseOverrides();
        BuildHandheldCatalog();
    }

    private void ClampWindowToScreen()
    {
        var maxWidth = Mathf.Max(420f, Screen.width - 16f);
        var maxHeight = Mathf.Max(500f, Screen.height - 16f);
        windowRect.width = Mathf.Min(windowRect.width, maxWidth);
        windowRect.height = Mathf.Min(windowRect.height, maxHeight);
        windowRect.x = Mathf.Clamp(windowRect.x, 8f, Mathf.Max(8f, Screen.width - windowRect.width - 8f));
        windowRect.y = Mathf.Clamp(windowRect.y, 8f, Mathf.Max(8f, Screen.height - windowRect.height - 8f));
    }

    private void InitStyles()
    {
        if (windowStyle != null) return;

        var surface = new Color(0.075f, 0.085f, 0.095f, 0.98f);
        var panel = new Color(0.105f, 0.12f, 0.135f, 0.96f);
        var panelHover = new Color(0.14f, 0.165f, 0.185f, 1f);
        var accent = new Color(0.08f, 0.62f, 0.78f, 1f);
        var accentHover = new Color(0.12f, 0.74f, 0.88f, 1f);
        var warmAccent = new Color(0.95f, 0.53f, 0.28f, 1f);

        windowStyle = new GUIStyle(GUI.skin.window);
        windowStyle.normal.background = MakeTex(2, 2, surface);
        windowStyle.normal.textColor = new Color(0.94f, 0.97f, 0.98f);
        windowStyle.fontStyle = FontStyle.Bold;
        windowStyle.fontSize = 17;
        windowStyle.padding = new RectOffset(18, 18, 34, 16);

        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.normal.background = MakeTex(2, 2, new Color(0.16f, 0.18f, 0.2f, 1f));
        buttonStyle.hover.background = MakeTex(2, 2, panelHover);
        buttonStyle.active.background = MakeTex(2, 2, new Color(0.19f, 0.24f, 0.27f, 1f));
        buttonStyle.normal.textColor = new Color(0.88f, 0.92f, 0.94f);
        buttonStyle.hover.textColor = Color.white;
        buttonStyle.fontStyle = FontStyle.Bold;
        buttonStyle.alignment = TextAnchor.MiddleCenter;
        buttonStyle.border = new RectOffset(6, 6, 6, 6);

        activeButtonStyle = new GUIStyle(buttonStyle);
        activeButtonStyle.normal.background = MakeTex(2, 2, accent);
        activeButtonStyle.hover.background = MakeTex(2, 2, accentHover);
        activeButtonStyle.normal.textColor = Color.white;

        smallButtonStyle = new GUIStyle(buttonStyle);
        smallButtonStyle.fontSize = 12;
        smallButtonStyle.padding = new RectOffset(8, 8, 4, 4);

        tileStyle = new GUIStyle(buttonStyle);
        tileStyle.normal.background = MakeTex(2, 2, panel);
        tileStyle.hover.background = MakeTex(2, 2, panelHover);
        tileStyle.active.background = MakeTex(2, 2, new Color(0.12f, 0.27f, 0.32f, 1f));
        tileStyle.normal.textColor = Color.white;
        tileStyle.alignment = TextAnchor.MiddleCenter;
        tileStyle.padding = new RectOffset(8, 8, 8, 8);

        iconBoxStyle = new GUIStyle(GUI.skin.box);
        iconBoxStyle.normal.background = MakeTex(2, 2, new Color(0.045f, 0.055f, 0.065f, 0.95f));
        iconBoxStyle.padding = new RectOffset(7, 7, 7, 7);
        iconBoxStyle.alignment = TextAnchor.MiddleCenter;

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.normal.textColor = new Color(0.84f, 0.88f, 0.9f);
        labelStyle.fontSize = 13;
        labelStyle.wordWrap = true;

        mutedLabelStyle = new GUIStyle(labelStyle);
        mutedLabelStyle.normal.textColor = new Color(0.55f, 0.62f, 0.66f);
        mutedLabelStyle.fontSize = 12;

        headerStyle = new GUIStyle(labelStyle);
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.fontSize = 15;
        headerStyle.normal.textColor = new Color(0.96f, 0.98f, 1f);

        tileLabelStyle = new GUIStyle(labelStyle);
        tileLabelStyle.alignment = TextAnchor.UpperCenter;
        tileLabelStyle.fontStyle = FontStyle.Bold;
        tileLabelStyle.fontSize = 12;
        tileLabelStyle.clipping = TextClipping.Clip;

        tabStyle = new GUIStyle(buttonStyle);
        tabStyle.fontSize = 13;
        tabStyle.normal.background = MakeTex(2, 2, new Color(0.10f, 0.115f, 0.13f, 1f));
        activeTabStyle = new GUIStyle(activeButtonStyle);
        activeTabStyle.fontSize = 13;

        panelStyle = new GUIStyle(GUI.skin.box);
        panelStyle.normal.background = MakeTex(2, 2, panel);
        panelStyle.padding = new RectOffset(12, 12, 10, 10);

        selectedPillStyle = new GUIStyle(labelStyle);
        selectedPillStyle.normal.background = MakeTex(2, 2, new Color(0.11f, 0.19f, 0.22f, 1f));
        selectedPillStyle.normal.textColor = new Color(0.94f, 1f, 1f);
        selectedPillStyle.fontStyle = FontStyle.Bold;
        selectedPillStyle.alignment = TextAnchor.MiddleLeft;
        selectedPillStyle.padding = new RectOffset(10, 10, 4, 4);
    }

    private Texture2D MakeTex(int width, int height, Color col)
    {
        Color[] pix = new Color[width * height];
        for (int i = 0; i < pix.Length; i++) pix[i] = col;
        Texture2D result = new Texture2D(width, height);
        result.SetPixels(pix);
        result.Apply();
        return result;
    }

    private void DrawWindow(int id)
    {
        GUILayout.Space(4f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Create", tab == 0 ? activeTabStyle : tabStyle, GUILayout.Height(32f))) tab = 0;
        if (GUILayout.Button("Edit", tab == 1 ? activeTabStyle : tabStyle, GUILayout.Height(32f))) tab = 1;
        if (GUILayout.Button("Held", tab == 2 ? activeTabStyle : tabStyle, GUILayout.Height(32f))) tab = 2;
        if (GUILayout.Button("Build", tab == 3 ? activeTabStyle : tabStyle, GUILayout.Height(32f))) tab = 3;
        GUILayout.EndHorizontal();
        GUILayout.Space(12f);

        GUILayout.BeginHorizontal();
        GUILayout.Label(selected != null ? selected.name : "Nothing selected", selectedPillStyle, GUILayout.Height(26f));
        if (GUILayout.Button("Refresh", smallButtonStyle, GUILayout.Width(78f), GUILayout.Height(26f)))
        {
            RefreshAll();
        }
        if (GUILayout.Button("Drop", smallButtonStyle, GUILayout.Width(58f), GUILayout.Height(26f)))
        {
            selected = null;
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(10f);

        GUILayout.BeginVertical(panelStyle);
        if (tab == 0)
        {
            DrawCreateTab();
        }
        else if (tab == 1)
        {
            DrawSceneTab();
        }
        else if (tab == 2)
        {
            DrawHandsTab();
        }
        else if (tab == 3)
        {
            DrawBuildTab();
        }
        GUILayout.EndVertical();

        if (tab == 1)
        {
            DrawSelectedTools();
        }

        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Close", activeButtonStyle, GUILayout.Width(96f), GUILayout.Height(30f)))
        {
            SetOpen(false);
        }
        GUILayout.EndHorizontal();

        GUI.DragWindow(new Rect(0f, 0f, 10000f, 32f));
    }

    private void DrawCreateTab()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Create", headerStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label(catalog.Count + " items", mutedLabelStyle, GUILayout.Width(62f));
        GUILayout.EndHorizontal();
        GUILayout.Space(8f);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Spawn scale", mutedLabelStyle, GUILayout.Width(86f));
        spawnScale = GUILayout.HorizontalSlider(spawnScale, 0.2f, 5f);
        GUILayout.Label(spawnScale.ToString("0.00"), labelStyle, GUILayout.Width(42f));
        GUILayout.EndHorizontal();

        // Search Bar
        GUILayout.BeginHorizontal();
        GUILayout.Label("Search", mutedLabelStyle, GUILayout.Width(86f));
        searchQuery = GUILayout.TextField(searchQuery, GUILayout.Height(20f));
        if (GUILayout.Button("X", smallButtonStyle, GUILayout.Width(22f), GUILayout.Height(20f)))
        {
            searchQuery = "";
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(6f);

        // Category Buttons
        GUILayout.BeginHorizontal();
        for (int i = 0; i < categories.Length; i++)
        {
            if (GUILayout.Button(categories[i], categoryIndex == i ? activeButtonStyle : buttonStyle, GUILayout.Height(22f)))
            {
                categoryIndex = i;
            }
        }
        GUILayout.EndHorizontal();

        // Filter the catalog
        var filteredCatalog = catalog.Where(item => {
            // Search filter
            if (!string.IsNullOrWhiteSpace(searchQuery) && !item.label.ToLowerInvariant().Contains(searchQuery.ToLowerInvariant()))
            {
                return false;
            }
            
            // Category filter
            if (categoryIndex == 0) return true; // All
            
            var name = item.template != null ? item.template.name.ToLowerInvariant() : item.label.ToLowerInvariant();
            
            if (categoryIndex == 1) // Seating
            {
                return name.Contains("chair") || name.Contains("sofa") || name.Contains("bench") || name.Contains("stool");
            }
            if (categoryIndex == 2) // Beds
            {
                return name.Contains("bed") || name.Contains("couch");
            }
            if (categoryIndex == 3) // Tables
            {
                return name.Contains("table") || name.Contains("desk") || name.Contains("cabinet") || name.Contains("bookcase") || name.Contains("shelf");
            }
            if (categoryIndex == 4) // Kitchen/Bath
            {
                return name.Contains("kitchen") || name.Contains("fridge") || name.Contains("stove") || name.Contains("sink") || name.Contains("microwave") || name.Contains("coffee") || name.Contains("bath") || name.Contains("shower") || name.Contains("toilet") || name.Contains("mirror");
            }
            if (categoryIndex == 5) // Office
            {
                return name.Contains("computer") || name.Contains("laptop") || name.Contains("keyboard") || name.Contains("mouse") || name.Contains("screen") || name.Contains("desk") || name.Contains("radio");
            }
            if (categoryIndex == 6) // Decor
            {
                return name.Contains("lamp") || name.Contains("plant") || name.Contains("screen") || name.Contains("laptop") || name.Contains("book") || name.Contains("tv") || name.Contains("television") || name.Contains("bear") || name.Contains("box") || name.Contains("radio") || name.Contains("rug") || name.Contains("speaker") || name.Contains("pillow") || name.Contains("trashcan") || name.Contains("rack");
            }
            
            return true;
        }).ToList();

        GUILayout.Space(10f);
        catalogScroll = GUILayout.BeginScrollView(catalogScroll, GUILayout.Height(250f));
        var columns = Mathf.Max(1, Mathf.FloorToInt((windowRect.width - 58f) / 112f));
        for (var i = 0; i < filteredCatalog.Count; i += columns)
        {
            GUILayout.BeginHorizontal();
            for (var j = 0; j < columns && i + j < filteredCatalog.Count; j++)
            {
                DrawCatalogTile(filteredCatalog[i + j]);
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
    }

    private void DrawCatalogTile(InventoryEntry entry)
    {
        GUILayout.BeginVertical(tileStyle, GUILayout.Width(104f), GUILayout.Height(118f));
        GUILayout.BeginVertical(iconBoxStyle, GUILayout.Width(88f), GUILayout.Height(70f));
        var content = new GUIContent(entry.icon, entry.label);
        if (GUILayout.Button(content, GUIStyle.none, GUILayout.Width(74f), GUILayout.Height(56f)))
        {
            SpawnFromTemplate(entry);
        }
        GUILayout.EndVertical();
        GUILayout.Space(4f);
        if (GUILayout.Button(entry.label, tileLabelStyle, GUILayout.Width(88f), GUILayout.Height(32f)))
        {
            SpawnFromTemplate(entry);
        }
        GUILayout.EndVertical();
        GUILayout.Space(6f);
    }

    private void DrawSceneTab()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("Scene", headerStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label(movables.Count + " placed", mutedLabelStyle, GUILayout.Width(78f));
        GUILayout.EndHorizontal();
        GUILayout.Space(8f);
        sceneScroll = GUILayout.BeginScrollView(sceneScroll, GUILayout.Height(338f));
        for (var i = 0; i < movables.Count; i++)
        {
            var item = movables[i];
            if (item == null)
            {
                continue;
            }

            GUILayout.BeginHorizontal();
            var icon = ResolveIcon(item.name, item);
            if (IsUsableCatalogIcon(icon))
            {
                GUILayout.Label(icon, iconBoxStyle, GUILayout.Width(38f), GUILayout.Height(38f));
            }
            else
            {
                GUILayout.Label(string.Empty, iconBoxStyle, GUILayout.Width(38f), GUILayout.Height(38f));
            }
            if (GUILayout.Button(item.name, item == selected ? activeButtonStyle : buttonStyle, GUILayout.Height(38f)))
            {
                Select(item);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(2f);
        }
        GUILayout.EndScrollView();
    }

    private void DrawHandsTab()
    {
        EnsureHandheldCatalog();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Hands", headerStyle);
        GUILayout.FlexibleSpace();
        var activeCharacter = GetActiveCharacter();
        GUILayout.Label(activeCharacter != null ? CleanLabel(activeCharacter.name) : "No character", mutedLabelStyle, GUILayout.Width(110f));
        GUILayout.EndHorizontal();
        GUILayout.Space(8f);

        GUILayout.BeginHorizontal();
        GUI.enabled = activeCharacter != null;
        if (GUILayout.Button("Empty hand", buttonStyle, GUILayout.Height(30f)))
        {
            ClearHeldItem(activeCharacter);
        }
        GUI.enabled = activeCharacter != null && selected != null;
        if (GUILayout.Button("Hold selected copy", buttonStyle, GUILayout.Height(30f)))
        {
            HoldSelectedCopy();
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.Space(10f);
        var heldItem = activeCharacter != null ? GetHeldItem(activeCharacter) : null;
        DrawHeldPoseEditor(heldItem);

        GUILayout.Space(10f);
        handsScroll = GUILayout.BeginScrollView(handsScroll, GUILayout.Height(190f));
        var columns = Mathf.Max(1, Mathf.FloorToInt((windowRect.width - 58f) / 112f));
        for (var i = 0; i < handheldCatalog.Count; i += columns)
        {
            GUILayout.BeginHorizontal();
            for (var j = 0; j < columns && i + j < handheldCatalog.Count; j++)
            {
                DrawHandheldTile(handheldCatalog[i + j]);
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
    }

    private void DrawHeldPoseEditor(StudioHeldItemMarker heldItem)
    {
        GUILayout.BeginVertical(panelStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Held item pose", headerStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label(heldItem != null ? heldItem.label : "Nothing held", mutedLabelStyle, GUILayout.Width(120f));
        GUILayout.EndHorizontal();

        if (heldItem == null)
        {
            GUILayout.Label("Hold an item, then tune its position here.", mutedLabelStyle);
            GUILayout.EndVertical();
            return;
        }

        var transformToEdit = heldItem.transform;
        var position = transformToEdit.localPosition;
        var euler = NormalizeEuler(transformToEdit.localEulerAngles);
        var scale = transformToEdit.localScale;
        var uniformScale = (scale.x + scale.y + scale.z) / 3f;

        GUILayout.Space(4f);
        position.x = DrawPoseSlider("Pos X", position.x, -0.35f, 0.35f);
        position.y = DrawPoseSlider("Pos Y", position.y, -0.35f, 0.35f);
        position.z = DrawPoseSlider("Pos Z", position.z, -0.35f, 0.35f);
        euler.x = DrawPoseSlider("Rot X", euler.x, -180f, 180f);
        euler.y = DrawPoseSlider("Rot Y", euler.y, -180f, 180f);
        euler.z = DrawPoseSlider("Rot Z", euler.z, -180f, 180f);
        uniformScale = DrawPoseSlider("Scale", uniformScale, 0.03f, 1.5f);

        transformToEdit.localPosition = position;
        transformToEdit.localRotation = Quaternion.Euler(euler);
        transformToEdit.localScale = Vector3.one * uniformScale;

        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save pose", activeButtonStyle, GUILayout.Height(30f)))
        {
            SaveHeldPose(heldItem);
        }
        if (GUILayout.Button("Reset pose", buttonStyle, GUILayout.Height(30f)))
        {
            ResetHeldPose(heldItem);
        }
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }

    private float DrawPoseSlider(string label, float value, float min, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, labelStyle, GUILayout.Width(48f));
        value = GUILayout.HorizontalSlider(value, min, max);
        GUILayout.Label(value.ToString("0.000"), mutedLabelStyle, GUILayout.Width(56f));
        GUILayout.EndHorizontal();
        return value;
    }

    private void DrawHandheldTile(HandheldEntry entry)
    {
        GUILayout.BeginVertical(tileStyle, GUILayout.Width(104f), GUILayout.Height(118f));
        GUILayout.BeginVertical(iconBoxStyle, GUILayout.Width(88f), GUILayout.Height(70f));
        var content = new GUIContent { image = GetHandheldIcon(entry), tooltip = entry.label };
        if (GUILayout.Button(content, GUIStyle.none, GUILayout.Width(74f), GUILayout.Height(56f)))
        {
            HoldGeneratedItem(entry);
        }
        GUILayout.EndVertical();
        GUILayout.Space(4f);
        if (GUILayout.Button(entry.label, tileLabelStyle, GUILayout.Width(88f), GUILayout.Height(32f)))
        {
            HoldGeneratedItem(entry);
        }
        GUILayout.EndVertical();
        GUILayout.Space(6f);
    }

    private void DrawBuildTab()
    {
        var builder = BlockBuildMode.Instance;
        if (builder == null)
        {
            GUILayout.Label("Build mode is loading...", labelStyle);
            return;
        }

        GUILayout.Label("Build", headerStyle);
        GUILayout.Space(8f);
        BlockBuildMode.IsBuildMode = GUILayout.Toggle(BlockBuildMode.IsBuildMode, " Cube build mode", labelStyle);

        GUILayout.Space(12f);
        GUILayout.Label("Block type", headerStyle);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Solid", builder.selectedKind == BlockBuildMode.BlockKind.Solid ? activeButtonStyle : buttonStyle, GUILayout.Height(30f)))
        {
            builder.SelectKind(BlockBuildMode.BlockKind.Solid);
        }
        if (GUILayout.Button("Water", builder.selectedKind == BlockBuildMode.BlockKind.Water ? activeButtonStyle : buttonStyle, GUILayout.Height(30f)))
        {
            builder.SelectKind(BlockBuildMode.BlockKind.Water);
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(12f);
        GUILayout.Label("Block color", headerStyle);
        GUILayout.Space(4f);
        DrawBlockColorPalette(builder);

        GUILayout.Space(12f);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Block size", headerStyle, GUILayout.Width(80f));
        builder.blockSize = GUILayout.HorizontalSlider(builder.blockSize, 0.25f, 3f);
        GUILayout.Label(builder.blockSize.ToString("0.00"), labelStyle, GUILayout.Width(42f));
        GUILayout.EndHorizontal();

        GUILayout.Space(12f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Undo last", buttonStyle, GUILayout.Height(30f)))
        {
            builder.UndoLastBlock();
        }
        if (GUILayout.Button("Clear spawned blocks", buttonStyle, GUILayout.Height(30f)))
        {
            builder.ClearBlocks();
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(10f);
        GUI.enabled = selected != null && IsBathLike(selected.name);
        if (GUILayout.Button("Fill selected bath with water", GUI.enabled ? activeButtonStyle : buttonStyle, GUILayout.Height(30f)))
        {
            FillSelectedBathWithWater(builder);
        }
        GUI.enabled = true;
    }


    private void DrawBlockColorPalette(BlockBuildMode builder)
    {
        if (builder.palette == null || builder.palette.Length == 0)
        {
            return;
        }

        var columns = Mathf.Max(1, Mathf.FloorToInt((windowRect.width - 44f) / 36f));
        for (var i = 0; i < builder.palette.Length; i += columns)
        {
            GUILayout.BeginHorizontal();
            for (var j = 0; j < columns && i + j < builder.palette.Length; j++)
            {
                var index = i + j;
                var oldColor = GUI.color;
                GUI.color = builder.palette[index];
                var label = builder.selectedColorIndex == index ? "*" : string.Empty;
                if (GUILayout.Button(label, GUILayout.Width(30f), GUILayout.Height(26f)))
                {
                    builder.SelectColor(index);
                }
                GUI.color = oldColor;
            }
            GUILayout.EndHorizontal();
        }

        var selected = builder.GetSelectedColor();
        var previous = GUI.color;
        GUI.color = selected;
        GUILayout.Box(string.Empty, GUILayout.Width(84f), GUILayout.Height(10f));
        GUI.color = previous;
    }

    private bool IsBathLike(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
        {
            return false;
        }

        var name = objectName.ToLowerInvariant();
        return name.Contains("bath") || name.Contains("tub");
    }

    private void FillSelectedBathWithWater(BlockBuildMode builder)
    {
        if (builder == null || selected == null || !TryGetRendererBounds(selected, out var bounds))
        {
            return;
        }

        var size = bounds.size;
        var waterSize = new Vector3(
            Mathf.Max(0.2f, size.x * 0.72f),
            Mathf.Max(0.035f, size.y * 0.12f),
            Mathf.Max(0.2f, size.z * 0.64f));
        var position = new Vector3(
            bounds.center.x,
            bounds.min.y + size.y * 0.58f,
            bounds.center.z);

        var existing = selected.Find("StudioBathWater");
        if (existing != null)
        {
            Destroy(existing.gameObject);
        }

        builder.CreateWaterVolume("StudioBathWater", position, waterSize, selected);
    }

    private bool TryGetRendererBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        if (root == null)
        {
            return false;
        }

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        var found = false;
        for (var i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null || renderer.GetComponentInParent<StudioWaterVolume>() != null)
            {
                continue;
            }

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }
    private void DrawSelectedTools()
    {
        GUILayout.Space(12f);
        GUI.enabled = selected != null;
        GUILayout.BeginVertical(panelStyle);
        GUILayout.Label("Selected tools", headerStyle);
        GUILayout.Space(6f);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Rotate -", buttonStyle, GUILayout.Height(28f)))
        {
            RotateSelected(-rotateStep);
        }
        if (GUILayout.Button("Rotate +", buttonStyle, GUILayout.Height(28f)))
        {
            RotateSelected(rotateStep);
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(4f);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Scale -", buttonStyle, GUILayout.Height(28f)))
        {
            ScaleSelected(selectedScale - scaleStep);
        }
        if (GUILayout.Button("Scale +", buttonStyle, GUILayout.Height(28f)))
        {
            ScaleSelected(selectedScale + scaleStep);
        }
        GUILayout.EndHorizontal();
        GUILayout.Space(4f);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Scale", headerStyle, GUILayout.Width(50f));
        var newScale = GUILayout.HorizontalSlider(selectedScale, minScale, maxScale);
        if (!Mathf.Approximately(newScale, selectedScale))
        {
            ScaleSelected(newScale);
        }
        GUILayout.Label(selectedScale.ToString("0.00"), labelStyle, GUILayout.Width(42f));
        GUILayout.EndHorizontal();

        GUILayout.Space(4f);
        if (GUILayout.Button("Grab (G)", activeButtonStyle, GUILayout.Height(30f)))
        {
            if (SurfaceItemPlacer.Instance != null)
            {
                SurfaceItemPlacer.Instance.StartPlacement(selected);
                SetOpen(false);
            }
        }

        GUILayout.EndVertical();
        GUI.enabled = true;
    }

    private void HandleKeyboardShortcuts(Keyboard keyboard)
    {
        if (selected == null)
        {
            return;
        }

        if (keyboard.leftBracketKey.wasPressedThisFrame)
        {
            ScaleSelected(selectedScale - scaleStep);
        }
        if (keyboard.rightBracketKey.wasPressedThisFrame)
        {
            ScaleSelected(selectedScale + scaleStep);
        }
        if (keyboard.rKey.wasPressedThisFrame)
        {
            RotateSelected(rotateStep);
        }
        if (keyboard.gKey.wasPressedThisFrame)
        {
            if (SurfaceItemPlacer.Instance != null)
            {
                SurfaceItemPlacer.Instance.StartPlacement(selected);
                SetOpen(false);
            }
        }
    }

    private void HandleMouseMoveAndPick()
    {
        var mouse = Mouse.current;
        var camera = Camera.main;
        if (mouse == null || camera == null || IsMouseInsideWindow(mouse) || BlockBuildMode.IsBuildMode || SurfaceItemPlacer.BlocksToolInput)
        {
            return;
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            TryPickObject(camera, mouse.position.ReadValue());
        }

        if (selected != null && mouse.leftButton.isPressed)
        {
            if (SurfaceItemPlacer.Instance != null)
            {
                SurfaceItemPlacer.Instance.StartPlacement(selected);
                SetOpen(false);
            }
        }
    }

    private bool IsMouseInsideWindow(Mouse mouse)
    {
        var position = mouse.position.ReadValue();
        var guiPosition = new Vector2(position.x, Screen.height - position.y);
        return windowRect.Contains(guiPosition);
    }

    private void TryPickObject(Camera camera, Vector2 mousePosition)
    {
        var ray = camera.ScreenPointToRay(mousePosition);
        if (!Physics.Raycast(ray, out var hit, 1000f, ~0, QueryTriggerInteraction.Collide))
        {
            return;
        }

        var root = FindMovableRoot(hit.transform);
        if (root != null)
        {
            Select(root);
        }
    }

    private Transform FindMovableRoot(Transform hit)
    {
        var current = hit;
        while (current != null)
        {
            if (movables.Contains(current))
            {
                return current;
            }
            current = current.parent;
        }

        return null;
    }

    private void MoveSelectedToMouse(Camera camera, Vector2 mousePosition)
    {
        var ray = camera.ScreenPointToRay(mousePosition);
        var plane = new Plane(Vector3.up, new Vector3(0f, moveGroundY, 0f));
        if (!plane.Raycast(ray, out var enter))
        {
            return;
        }

        var point = ray.GetPoint(enter);
        var position = selected.position;
        position.x = point.x;
        position.z = point.z;
        selected.position = position;
    }

    private void SpawnFromTemplate(InventoryEntry entry)
    {
        if (entry == null || entry.template == null)
        {
            return;
        }

        var clone = Instantiate(entry.template.gameObject);
        clone.name = MakeSpawnName(entry.label);
        clone.transform.SetParent(GetSpawnParent(), true);
        clone.transform.position = GetSpawnPoint();
        clone.transform.rotation = entry.template.rotation;
        clone.transform.localScale = entry.template.localScale * spawnScale;
        clone.SetActive(true);
        FitSpawnedObjectToCharacterScale(clone, entry.label);
        PrepareSpawnedObject(clone);
        Select(clone.transform);
        RefreshAll();
        if (SurfaceItemPlacer.Instance != null)
        {
            SurfaceItemPlacer.Instance.StartPlacement(clone.transform);
            SetOpen(false);
        }
    }

    private void HoldGeneratedItem(HandheldEntry entry)
    {
        var activeCharacter = GetActiveCharacter();
        if (entry == null || activeCharacter == null)
        {
            return;
        }

        var heldObject = CreateHandheldObject(entry);
        var pose = ResolveHandheldPose(activeCharacter, entry.label, entry.localPosition, entry.localEuler, entry.localScale);
        AttachToHand(activeCharacter, heldObject, pose, entry.label, entry.localPosition, entry.localEuler, entry.localScale);
    }

    private void HoldSelectedCopy()
    {
        var activeCharacter = GetActiveCharacter();
        if (activeCharacter == null || selected == null)
        {
            return;
        }

        var clone = Instantiate(selected.gameObject);
        var label = "Scene copy: " + CleanLabel(selected.name);
        clone.name = "Held " + CleanLabel(selected.name);
        PrepareHeldObject(clone);
        var defaultPosition = new Vector3(0.055f, 0.02f, 0.085f);
        var defaultEuler = new Vector3(74f, 0f, 15f);
        var defaultScale = Vector3.one * 0.32f;
        var pose = ResolveHandheldPose(activeCharacter, label, defaultPosition, defaultEuler, defaultScale);
        AttachToHand(
            activeCharacter,
            clone,
            pose,
            label,
            defaultPosition,
            defaultEuler,
            defaultScale);
    }

    private GameObject CreateHandheldObject(HandheldEntry entry)
    {
        var root = new GameObject("Held " + entry.label);
        switch (entry.shape)
        {
            case HandheldShape.Apple:
                AddPrimitive(root.transform, PrimitiveType.Sphere, "AppleBody", Vector3.zero, Quaternion.identity, new Vector3(1f, 0.92f, 1f), entry.color, 0f, 0.48f);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "AppleTopDimple", new Vector3(0f, 0.47f, 0f), Quaternion.identity, new Vector3(0.36f, 0.12f, 0.36f), new Color(0.54f, 0.04f, 0.035f), 0f, 0.3f);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "AppleHighlight", new Vector3(-0.25f, 0.18f, -0.32f), Quaternion.identity, new Vector3(0.18f, 0.1f, 0.06f), new Color(1f, 0.82f, 0.72f), 0f, 0.7f);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Stem", new Vector3(0f, 0.62f, 0f), Quaternion.Euler(12f, 0f, -18f), new Vector3(0.11f, 0.28f, 0.11f), new Color(0.34f, 0.18f, 0.08f));
                AddPrimitive(root.transform, PrimitiveType.Cube, "Leaf", new Vector3(0.24f, 0.76f, 0f), Quaternion.Euler(0f, 0f, 28f), new Vector3(0.34f, 0.055f, 0.16f), new Color(0.20f, 0.62f, 0.26f), 0f, 0.35f);
                break;
            case HandheldShape.GolfClub:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Shaft", new Vector3(0f, 0f, 0f), Quaternion.identity, new Vector3(0.055f, 2.35f, 0.055f), new Color(0.78f, 0.81f, 0.82f), 0.75f, 0.62f);
                AddPrimitive(root.transform, PrimitiveType.Cube, "ClubHead", new Vector3(0.34f, -1.18f, 0f), Quaternion.Euler(0f, 0f, -13f), new Vector3(0.72f, 0.18f, 0.26f), new Color(0.50f, 0.52f, 0.54f), 0.9f, 0.55f);
                AddPrimitive(root.transform, PrimitiveType.Cube, "ClubFace", new Vector3(0.47f, -1.08f, -0.15f), Quaternion.Euler(0f, 0f, -13f), new Vector3(0.48f, 0.08f, 0.035f), new Color(0.86f, 0.87f, 0.84f), 1f, 0.7f);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Grip", new Vector3(0f, 1.18f, 0f), Quaternion.identity, new Vector3(0.12f, 0.38f, 0.12f), new Color(0.035f, 0.035f, 0.04f), 0f, 0.25f);
                break;
            case HandheldShape.Phone:
                AddPrimitive(root.transform, PrimitiveType.Cube, "PhoneBody", Vector3.zero, Quaternion.identity, new Vector3(0.58f, 0.98f, 0.085f), new Color(0.025f, 0.03f, 0.04f), 0f, 0.65f);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Screen", new Vector3(0f, 0.03f, -0.05f), Quaternion.identity, new Vector3(0.47f, 0.76f, 0.014f), new Color(0.03f, 0.42f, 0.58f), 0f, 0.82f);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "HomeButton", new Vector3(0f, -0.42f, -0.06f), Quaternion.identity, new Vector3(0.08f, 0.08f, 0.014f), new Color(0.11f, 0.12f, 0.13f), 0f, 0.5f);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "CameraDot", new Vector3(0.18f, 0.41f, -0.06f), Quaternion.identity, new Vector3(0.06f, 0.06f, 0.014f), new Color(0.02f, 0.02f, 0.025f), 0f, 0.55f);
                break;
            case HandheldShape.Microphone:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Handle", new Vector3(0f, -0.38f, 0f), Quaternion.identity, new Vector3(0.20f, 0.72f, 0.20f), new Color(0.045f, 0.05f, 0.055f), 0f, 0.36f);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Ring", new Vector3(0f, 0.04f, 0f), Quaternion.identity, new Vector3(0.26f, 0.08f, 0.26f), new Color(0.78f, 0.80f, 0.82f), 0.8f, 0.5f);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "MicHead", new Vector3(0f, 0.42f, 0f), Quaternion.identity, new Vector3(0.50f, 0.46f, 0.50f), new Color(0.13f, 0.13f, 0.14f), 0.2f, 0.45f);
                break;
            case HandheldShape.Bottle:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "BottleBody", new Vector3(0f, -0.08f, 0f), Quaternion.identity, new Vector3(0.40f, 0.82f, 0.40f), entry.color, 0f, 0.55f);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "BottleNeck", new Vector3(0f, 0.78f, 0f), Quaternion.identity, new Vector3(0.22f, 0.28f, 0.22f), new Color(0.16f, 0.54f, 0.80f), 0f, 0.6f);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Cap", new Vector3(0f, 1.08f, 0f), Quaternion.identity, new Vector3(0.24f, 0.14f, 0.24f), new Color(0.05f, 0.08f, 0.13f), 0f, 0.35f);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Label", new Vector3(0f, -0.08f, -0.41f), Quaternion.identity, new Vector3(0.50f, 0.34f, 0.025f), new Color(0.94f, 0.96f, 0.88f), 0f, 0.45f);
                break;
            case HandheldShape.Cup:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "CupBody", Vector3.zero, Quaternion.identity, new Vector3(0.46f, 0.58f, 0.46f), entry.color, 0f, 0.62f);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "CupRim", new Vector3(0f, 0.60f, 0f), Quaternion.identity, new Vector3(0.53f, 0.05f, 0.53f), new Color(1f, 1f, 0.96f), 0f, 0.72f);
                AddPrimitive(root.transform, PrimitiveType.Cube, "HandleTop", new Vector3(0.45f, 0.19f, 0f), Quaternion.identity, new Vector3(0.20f, 0.08f, 0.12f), entry.color, 0f, 0.55f);
                AddPrimitive(root.transform, PrimitiveType.Cube, "HandleSide", new Vector3(0.58f, 0f, 0f), Quaternion.identity, new Vector3(0.08f, 0.36f, 0.12f), entry.color, 0f, 0.55f);
                AddPrimitive(root.transform, PrimitiveType.Cube, "HandleBottom", new Vector3(0.45f, -0.19f, 0f), Quaternion.identity, new Vector3(0.20f, 0.08f, 0.12f), entry.color, 0f, 0.55f);
                break;
            case HandheldShape.Book:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Cover", Vector3.zero, Quaternion.identity, new Vector3(0.74f, 0.98f, 0.16f), entry.color, 0f, 0.4f);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Pages", new Vector3(0.055f, 0f, -0.095f), Quaternion.identity, new Vector3(0.58f, 0.84f, 0.04f), new Color(0.93f, 0.88f, 0.72f), 0f, 0.35f);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Spine", new Vector3(-0.36f, 0f, -0.01f), Quaternion.identity, new Vector3(0.08f, 1.02f, 0.18f), new Color(0.38f, 0.04f, 0.08f), 0f, 0.35f);
                AddPrimitive(root.transform, PrimitiveType.Cube, "TitleBar", new Vector3(0.05f, 0.18f, -0.105f), Quaternion.identity, new Vector3(0.36f, 0.08f, 0.03f), new Color(0.96f, 0.76f, 0.26f), 0f, 0.45f);
                break;
            case HandheldShape.Flashlight:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "GripBody", Vector3.zero, Quaternion.Euler(90f, 0f, 0f), new Vector3(0.25f, 0.78f, 0.25f), entry.color, 0.2f, 0.45f);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Head", new Vector3(0f, 0f, 0.70f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.36f, 0.22f, 0.36f), new Color(0.12f, 0.12f, 0.13f), 0.35f, 0.5f);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Lens", new Vector3(0f, 0f, 0.92f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.30f, 0.04f, 0.30f), new Color(1f, 0.96f, 0.62f), 0f, 0.9f);
                break;
            case HandheldShape.Tablet:
                AddPrimitive(root.transform, PrimitiveType.Cube, "TabletBody", Vector3.zero, Quaternion.identity, new Vector3(0.78f, 1.08f, 0.06f), new Color(0.025f, 0.03f, 0.04f), 0f, 0.65f);
                AddPrimitive(root.transform, PrimitiveType.Cube, "TabletScreen", new Vector3(0f, 0f, -0.04f), Quaternion.identity, new Vector3(0.64f, 0.86f, 0.012f), new Color(0.03f, 0.34f, 0.52f), 0f, 0.75f);
                break;
            case HandheldShape.Notebook:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Cover", Vector3.zero, Quaternion.identity, new Vector3(0.66f, 0.90f, 0.13f), entry.color, 0f, 0.4f);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Pages", new Vector3(0.055f, 0f, -0.08f), Quaternion.identity, new Vector3(0.50f, 0.76f, 0.035f), new Color(0.93f, 0.91f, 0.82f), 0f, 0.35f);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Spine", new Vector3(-0.33f, 0f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.045f, 0.88f, 0.045f), entry.color * 0.75f, 0f, 0.3f);
                break;
            case HandheldShape.Keys:
                AddPrimitive(root.transform, PrimitiveType.Sphere, "RingA", new Vector3(-0.12f, 0.18f, 0f), Quaternion.identity, new Vector3(0.18f, 0.04f, 0.18f), entry.color, 0.85f, 0.55f);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "RingB", new Vector3(0.12f, 0.18f, 0f), Quaternion.identity, new Vector3(0.18f, 0.04f, 0.18f), entry.color, 0.85f, 0.55f);
                AddPrimitive(root.transform, PrimitiveType.Cube, "KeyA", new Vector3(0.20f, -0.12f, 0f), Quaternion.Euler(0f, 0f, -22f), new Vector3(0.11f, 0.46f, 0.035f), entry.color, 0.85f, 0.55f);
                AddPrimitive(root.transform, PrimitiveType.Cube, "KeyB", new Vector3(-0.12f, -0.14f, 0.01f), Quaternion.Euler(0f, 0f, 18f), new Vector3(0.10f, 0.38f, 0.035f), entry.color * 0.9f, 0.85f, 0.55f);
                break;
            case HandheldShape.SodaCan:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Can", Vector3.zero, Quaternion.identity, new Vector3(0.36f, 0.62f, 0.36f), entry.color, 0.1f, 0.42f);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Top", new Vector3(0f, 0.33f, 0f), Quaternion.identity, new Vector3(0.37f, 0.04f, 0.37f), new Color(0.76f, 0.77f, 0.78f), 0.8f, 0.55f);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Label", new Vector3(0f, 0f, -0.19f), Quaternion.identity, new Vector3(0.23f, 0.28f, 0.018f), Color.white, 0f, 0.4f);
                break;
            case HandheldShape.PizzaSlice:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Slice", Vector3.zero, Quaternion.Euler(0f, 0f, 42f), new Vector3(0.78f, 0.44f, 0.08f), entry.color, 0f, 0.45f);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Crust", new Vector3(0.26f, 0.24f, 0.01f), Quaternion.Euler(0f, 0f, 42f), new Vector3(0.48f, 0.10f, 0.09f), new Color(0.62f, 0.34f, 0.13f), 0f, 0.35f);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Pepperoni", new Vector3(-0.08f, -0.02f, -0.05f), Quaternion.identity, new Vector3(0.14f, 0.14f, 0.035f), new Color(0.68f, 0.08f, 0.06f), 0f, 0.45f);
                break;
            case HandheldShape.Donut:
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Donut", Vector3.zero, Quaternion.identity, new Vector3(0.58f, 0.18f, 0.58f), entry.color, 0f, 0.35f);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Hole", new Vector3(0f, 0.02f, 0f), Quaternion.identity, new Vector3(0.24f, 0.20f, 0.24f), new Color(0.10f, 0.075f, 0.055f), 0f, 0.25f);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Icing", new Vector3(0f, 0.08f, 0f), Quaternion.identity, new Vector3(0.46f, 0.06f, 0.46f), new Color(0.96f, 0.42f, 0.70f), 0f, 0.5f);
                break;
            case HandheldShape.GameController:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Body", Vector3.zero, Quaternion.identity, new Vector3(0.82f, 0.32f, 0.14f), entry.color, 0f, 0.5f);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "LeftGrip", new Vector3(-0.42f, -0.03f, 0f), Quaternion.identity, new Vector3(0.26f, 0.30f, 0.16f), entry.color, 0f, 0.5f);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "RightGrip", new Vector3(0.42f, -0.03f, 0f), Quaternion.identity, new Vector3(0.26f, 0.30f, 0.16f), entry.color, 0f, 0.5f);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "ButtonA", new Vector3(0.22f, 0.07f, -0.09f), Quaternion.identity, new Vector3(0.07f, 0.07f, 0.025f), new Color(0.1f, 0.7f, 0.22f), 0f, 0.55f);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "ButtonB", new Vector3(0.36f, -0.03f, -0.09f), Quaternion.identity, new Vector3(0.07f, 0.07f, 0.025f), new Color(0.8f, 0.12f, 0.12f), 0f, 0.55f);
                break;
            case HandheldShape.Headphones:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "BandTop", new Vector3(0f, 0.24f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.045f, 0.62f, 0.045f), entry.color, 0f, 0.5f);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "LeftCup", new Vector3(-0.36f, -0.06f, 0f), Quaternion.identity, new Vector3(0.20f, 0.30f, 0.20f), entry.color, 0f, 0.45f);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "RightCup", new Vector3(0.36f, -0.06f, 0f), Quaternion.identity, new Vector3(0.20f, 0.30f, 0.20f), entry.color, 0f, 0.45f);
                break;
            case HandheldShape.Candle:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Wax", Vector3.zero, Quaternion.identity, new Vector3(0.32f, 0.58f, 0.32f), entry.color, 0f, 0.35f);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Wick", new Vector3(0f, 0.34f, 0f), Quaternion.identity, new Vector3(0.035f, 0.16f, 0.035f), new Color(0.08f, 0.06f, 0.04f), 0f, 0.2f);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Flame", new Vector3(0f, 0.48f, 0f), Quaternion.identity, new Vector3(0.14f, 0.22f, 0.14f), new Color(1f, 0.62f, 0.08f), 0f, 0.7f);
                break;
            case HandheldShape.PictureFrame:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Frame", Vector3.zero, Quaternion.identity, new Vector3(0.86f, 0.62f, 0.07f), entry.color, 0f, 0.35f);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Picture", new Vector3(0f, 0f, -0.05f), Quaternion.identity, new Vector3(0.68f, 0.44f, 0.018f), new Color(0.36f, 0.56f, 0.70f), 0f, 0.45f);
                break;
            case HandheldShape.Bowl:
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Bowl", Vector3.zero, Quaternion.identity, new Vector3(0.62f, 0.30f, 0.62f), entry.color, 0f, 0.35f);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Rim", new Vector3(0f, 0.16f, 0f), Quaternion.identity, new Vector3(0.66f, 0.05f, 0.66f), entry.color * 0.92f, 0f, 0.35f);
                break;
            case HandheldShape.Spoon:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Handle", new Vector3(0f, 0.20f, 0f), Quaternion.identity, new Vector3(0.045f, 0.74f, 0.045f), entry.color, 0.7f, 0.55f);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "SpoonBowl", new Vector3(0f, -0.28f, 0f), Quaternion.identity, new Vector3(0.18f, 0.24f, 0.045f), entry.color, 0.7f, 0.55f);
                break;
            case HandheldShape.Toothbrush:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Handle", Vector3.zero, Quaternion.identity, new Vector3(0.11f, 0.86f, 0.08f), entry.color, 0f, 0.45f);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Head", new Vector3(0f, 0.48f, 0f), Quaternion.identity, new Vector3(0.18f, 0.20f, 0.08f), Color.white, 0f, 0.45f);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Bristles", new Vector3(0f, 0.55f, -0.08f), Quaternion.identity, new Vector3(0.15f, 0.12f, 0.06f), new Color(0.75f, 0.90f, 1f), 0f, 0.45f);
                break;
            case HandheldShape.Pen:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Body", Vector3.zero, Quaternion.identity, new Vector3(0.07f, 0.86f, 0.07f), entry.color, 0.25f, 0.48f);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Tip", new Vector3(0f, -0.48f, 0f), Quaternion.identity, new Vector3(0.055f, 0.12f, 0.055f), new Color(0.70f, 0.70f, 0.72f), 0.7f, 0.55f);
                break;
        }

        PrepareHeldObject(root);
        return root;
    }

    private void BuildHandheldCatalog()
    {
        handheldCatalog.Clear();
        AddHandheld("Apple", HandheldShape.Apple, new Color(0.86f, 0.08f, 0.05f), new Vector3(0.055f, 0.02f, 0.085f), new Vector3(74f, 0f, 12f), Vector3.one * 0.16f);
        AddHandheld("Golf club", HandheldShape.GolfClub, new Color(0.72f, 0.75f, 0.76f), new Vector3(0.058f, 0.01f, 0.095f), new Vector3(24f, 0f, -22f), Vector3.one * 0.42f);
        AddHandheld("Phone", HandheldShape.Phone, new Color(0.03f, 0.035f, 0.045f), new Vector3(0.055f, 0.02f, 0.084f), new Vector3(82f, 0f, 12f), Vector3.one * 0.17f);
        AddHandheld("Microphone", HandheldShape.Microphone, new Color(0.10f, 0.11f, 0.12f), new Vector3(0.05f, 0.018f, 0.088f), new Vector3(58f, 0f, 8f), Vector3.one * 0.20f);
        AddHandheld("Water bottle", HandheldShape.Bottle, new Color(0.18f, 0.62f, 0.88f), new Vector3(0.052f, 0.018f, 0.086f), new Vector3(66f, 0f, 8f), Vector3.one * 0.16f);
        AddHandheld("Coffee cup", HandheldShape.Cup, new Color(0.94f, 0.94f, 0.88f), new Vector3(0.052f, 0.018f, 0.085f), new Vector3(72f, 0f, 10f), Vector3.one * 0.18f);
        AddHandheld("Book", HandheldShape.Book, new Color(0.72f, 0.12f, 0.17f), new Vector3(0.032f, 0.018f, 0.082f), new Vector3(76f, 0f, 12f), Vector3.one * 0.42f);
        AddHandheld("Flashlight", HandheldShape.Flashlight, new Color(0.18f, 0.18f, 0.20f), new Vector3(0.052f, 0.018f, 0.09f), new Vector3(80f, 0f, 12f), Vector3.one * 0.18f);
        AddHandheld("Tablet", HandheldShape.Tablet, new Color(0.025f, 0.03f, 0.04f), new Vector3(0.044f, 0.018f, 0.082f), new Vector3(78f, 0f, 12f), Vector3.one * 0.26f);
        AddHandheld("Notebook", HandheldShape.Notebook, new Color(0.16f, 0.34f, 0.74f), new Vector3(0.036f, 0.018f, 0.082f), new Vector3(76f, 0f, 12f), Vector3.one * 0.34f);
        AddHandheld("Keys", HandheldShape.Keys, new Color(0.95f, 0.74f, 0.22f), new Vector3(0.052f, 0.016f, 0.088f), new Vector3(74f, 0f, 12f), Vector3.one * 0.18f);
        AddHandheld("Soda can", HandheldShape.SodaCan, new Color(0.82f, 0.06f, 0.08f), new Vector3(0.052f, 0.018f, 0.086f), new Vector3(68f, 0f, 8f), Vector3.one * 0.18f);
        AddHandheld("Pizza slice", HandheldShape.PizzaSlice, new Color(0.95f, 0.62f, 0.22f), new Vector3(0.052f, 0.016f, 0.085f), new Vector3(76f, 0f, 16f), Vector3.one * 0.28f);
        AddHandheld("Donut", HandheldShape.Donut, new Color(0.72f, 0.38f, 0.18f), new Vector3(0.052f, 0.017f, 0.086f), new Vector3(80f, 0f, 10f), Vector3.one * 0.20f);
        AddHandheld("Gamepad", HandheldShape.GameController, new Color(0.04f, 0.045f, 0.055f), new Vector3(0.045f, 0.017f, 0.084f), new Vector3(82f, 0f, 10f), Vector3.one * 0.22f);
        AddHandheld("Headphones", HandheldShape.Headphones, new Color(0.06f, 0.065f, 0.075f), new Vector3(0.044f, 0.018f, 0.084f), new Vector3(78f, 0f, 12f), Vector3.one * 0.23f);
        AddHandheld("Candle", HandheldShape.Candle, new Color(0.98f, 0.86f, 0.55f), new Vector3(0.052f, 0.018f, 0.086f), new Vector3(68f, 0f, 8f), Vector3.one * 0.17f);
        AddHandheld("Photo frame", HandheldShape.PictureFrame, new Color(0.56f, 0.36f, 0.18f), new Vector3(0.038f, 0.017f, 0.082f), new Vector3(78f, 0f, 12f), Vector3.one * 0.28f);
        AddHandheld("Bowl", HandheldShape.Bowl, new Color(0.94f, 0.90f, 0.82f), new Vector3(0.052f, 0.017f, 0.086f), new Vector3(76f, 0f, 10f), Vector3.one * 0.20f);
        AddHandheld("Spoon", HandheldShape.Spoon, new Color(0.76f, 0.78f, 0.80f), new Vector3(0.052f, 0.015f, 0.087f), new Vector3(64f, 0f, 8f), Vector3.one * 0.20f);
        AddHandheld("Toothbrush", HandheldShape.Toothbrush, new Color(0.08f, 0.62f, 0.82f), new Vector3(0.052f, 0.015f, 0.087f), new Vector3(68f, 0f, 8f), Vector3.one * 0.18f);
        AddHandheld("Pen", HandheldShape.Pen, new Color(0.02f, 0.025f, 0.03f), new Vector3(0.052f, 0.015f, 0.087f), new Vector3(68f, 0f, 8f), Vector3.one * 0.18f);
    }

    private void EnsureHandheldCatalog()
    {
        if (handheldCatalog.Count == 0)
        {
            BuildHandheldCatalog();
        }
    }

    private void AddHandheld(string label, HandheldShape shape, Color color, Vector3 localPosition, Vector3 localEuler, Vector3 localScale)
    {
        handheldCatalog.Add(new HandheldEntry
        {
            label = label,
            shape = shape,
            color = color,
            localPosition = localPosition,
            localEuler = localEuler,
            localScale = localScale
        });
    }

    private Texture2D GetHandheldIcon(HandheldEntry entry)
    {
        if (entry == null)
        {
            return Texture2D.whiteTexture;
        }

        if (handheldIconCache.TryGetValue(entry.label, out var cached))
        {
            return cached;
        }

        var icon = RenderHandheldIcon(entry);
        icon.name = "HandheldIcon_" + entry.label;
        handheldIconCache[entry.label] = icon;
        return icon;
    }

    private Texture2D RenderHandheldIcon(HandheldEntry entry)
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var background = new Color(0.055f, 0.065f, 0.075f, 1f);
        var shadow = new Color(0f, 0f, 0f, 0.28f);
        var light = new Color(1f, 1f, 1f, 0.9f);

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                texture.SetPixel(x, y, background);
            }
        }

        switch (entry.shape)
        {
            case HandheldShape.Apple:
                DrawCircle(texture, 34, 30, 17, shadow);
                DrawCircle(texture, 31, 33, 17, entry.color);
                DrawRect(texture, 30, 48, 4, 9, new Color(0.34f, 0.18f, 0.08f));
                DrawRect(texture, 36, 51, 13, 4, new Color(0.20f, 0.62f, 0.26f));
                DrawCircle(texture, 24, 40, 4, new Color(1f, 0.78f, 0.70f, 1f));
                break;
            case HandheldShape.GolfClub:
                DrawLine(texture, 22, 54, 40, 10, new Color(0.78f, 0.81f, 0.82f), 2);
                DrawRect(texture, 34, 8, 22, 7, new Color(0.50f, 0.52f, 0.54f));
                DrawRect(texture, 18, 49, 8, 12, new Color(0.035f, 0.035f, 0.04f));
                break;
            case HandheldShape.Phone:
                DrawRect(texture, 23, 10, 19, 42, new Color(0.02f, 0.025f, 0.032f));
                DrawRect(texture, 26, 15, 13, 29, new Color(0.03f, 0.42f, 0.58f));
                DrawCircle(texture, 32, 48, 2, new Color(0.14f, 0.15f, 0.16f));
                break;
            case HandheldShape.Microphone:
                DrawRect(texture, 29, 29, 7, 25, new Color(0.045f, 0.05f, 0.055f));
                DrawCircle(texture, 32, 22, 12, new Color(0.13f, 0.13f, 0.14f));
                DrawRect(texture, 24, 32, 16, 4, new Color(0.78f, 0.80f, 0.82f));
                break;
            case HandheldShape.Bottle:
                DrawRect(texture, 25, 21, 16, 31, entry.color);
                DrawRect(texture, 28, 11, 10, 12, new Color(0.16f, 0.54f, 0.80f));
                DrawRect(texture, 27, 9, 12, 5, new Color(0.05f, 0.08f, 0.13f));
                DrawRect(texture, 23, 32, 20, 10, new Color(0.94f, 0.96f, 0.88f));
                break;
            case HandheldShape.Cup:
                DrawRect(texture, 23, 20, 22, 28, entry.color);
                DrawRect(texture, 21, 47, 26, 5, light);
                DrawRect(texture, 44, 29, 11, 4, entry.color);
                DrawRect(texture, 52, 29, 4, 13, entry.color);
                DrawRect(texture, 44, 39, 11, 4, entry.color);
                break;
            case HandheldShape.Book:
                DrawRect(texture, 20, 12, 26, 40, entry.color);
                DrawRect(texture, 22, 15, 18, 34, new Color(0.93f, 0.88f, 0.72f));
                DrawRect(texture, 18, 11, 5, 42, new Color(0.38f, 0.04f, 0.08f));
                DrawRect(texture, 26, 29, 13, 4, new Color(0.96f, 0.76f, 0.26f));
                break;
            case HandheldShape.Flashlight:
                DrawLine(texture, 20, 42, 43, 19, entry.color, 7);
                DrawCircle(texture, 46, 16, 8, new Color(0.12f, 0.12f, 0.13f));
                DrawCircle(texture, 47, 15, 5, new Color(1f, 0.96f, 0.62f));
                break;
            case HandheldShape.Tablet:
                DrawRect(texture, 22, 8, 22, 48, new Color(0.02f, 0.025f, 0.032f));
                DrawRect(texture, 25, 13, 16, 38, new Color(0.03f, 0.34f, 0.52f));
                break;
            case HandheldShape.Notebook:
                DrawRect(texture, 19, 14, 28, 36, entry.color);
                DrawRect(texture, 23, 17, 19, 30, new Color(0.93f, 0.91f, 0.82f));
                DrawRect(texture, 17, 13, 5, 38, entry.color * 0.75f);
                break;
            case HandheldShape.Keys:
                DrawCircle(texture, 27, 22, 8, entry.color);
                DrawCircle(texture, 37, 22, 8, entry.color);
                DrawRect(texture, 36, 29, 6, 24, entry.color);
                DrawRect(texture, 23, 31, 6, 20, entry.color * 0.9f);
                break;
            case HandheldShape.SodaCan:
                DrawRect(texture, 24, 17, 18, 36, entry.color);
                DrawRect(texture, 23, 14, 20, 5, new Color(0.75f, 0.76f, 0.76f));
                DrawRect(texture, 26, 29, 14, 14, Color.white);
                break;
            case HandheldShape.PizzaSlice:
                DrawRect(texture, 18, 27, 34, 20, entry.color);
                DrawRect(texture, 31, 22, 22, 6, new Color(0.62f, 0.34f, 0.13f));
                DrawCircle(texture, 29, 37, 4, new Color(0.68f, 0.08f, 0.06f));
                break;
            case HandheldShape.Donut:
                DrawCircle(texture, 32, 33, 17, entry.color);
                DrawCircle(texture, 32, 32, 11, new Color(0.96f, 0.42f, 0.70f));
                DrawCircle(texture, 32, 33, 5, new Color(0.10f, 0.075f, 0.055f));
                break;
            case HandheldShape.GameController:
                DrawRect(texture, 16, 27, 34, 16, entry.color);
                DrawCircle(texture, 18, 36, 8, entry.color);
                DrawCircle(texture, 48, 36, 8, entry.color);
                DrawCircle(texture, 40, 31, 3, new Color(0.1f, 0.7f, 0.22f));
                DrawCircle(texture, 47, 36, 3, new Color(0.8f, 0.12f, 0.12f));
                break;
            case HandheldShape.Headphones:
                DrawRect(texture, 19, 16, 28, 6, entry.color);
                DrawRect(texture, 16, 24, 7, 24, entry.color);
                DrawRect(texture, 44, 24, 7, 24, entry.color);
                DrawRect(texture, 13, 34, 12, 16, new Color(0.12f, 0.13f, 0.14f));
                DrawRect(texture, 42, 34, 12, 16, new Color(0.12f, 0.13f, 0.14f));
                break;
            case HandheldShape.Candle:
                DrawRect(texture, 25, 23, 18, 29, entry.color);
                DrawRect(texture, 31, 17, 3, 8, new Color(0.08f, 0.06f, 0.04f));
                DrawCircle(texture, 32, 13, 6, new Color(1f, 0.62f, 0.08f));
                break;
            case HandheldShape.PictureFrame:
                DrawRect(texture, 15, 18, 38, 28, entry.color);
                DrawRect(texture, 20, 22, 28, 20, new Color(0.36f, 0.56f, 0.70f));
                break;
            case HandheldShape.Bowl:
                DrawRect(texture, 20, 32, 26, 14, entry.color);
                DrawRect(texture, 18, 27, 30, 5, entry.color * 0.92f);
                break;
            case HandheldShape.Spoon:
                DrawLine(texture, 32, 13, 32, 50, entry.color, 4);
                DrawCircle(texture, 32, 15, 8, entry.color);
                break;
            case HandheldShape.Toothbrush:
                DrawRect(texture, 29, 15, 7, 37, entry.color);
                DrawRect(texture, 26, 10, 13, 11, Color.white);
                DrawRect(texture, 27, 7, 11, 6, new Color(0.75f, 0.90f, 1f));
                break;
            case HandheldShape.Pen:
                DrawLine(texture, 31, 10, 33, 53, entry.color, 4);
                DrawRect(texture, 29, 51, 8, 6, new Color(0.70f, 0.70f, 0.72f));
                break;
        }

        texture.Apply(false, true);
        return texture;
    }

    private void DrawRect(Texture2D texture, int x, int y, int width, int height, Color color)
    {
        for (var yy = Mathf.Max(0, y); yy < Mathf.Min(texture.height, y + height); yy++)
        {
            for (var xx = Mathf.Max(0, x); xx < Mathf.Min(texture.width, x + width); xx++)
            {
                texture.SetPixel(xx, yy, color);
            }
        }
    }

    private void DrawCircle(Texture2D texture, int cx, int cy, int radius, Color color)
    {
        var radiusSq = radius * radius;
        for (var y = cy - radius; y <= cy + radius; y++)
        {
            for (var x = cx - radius; x <= cx + radius; x++)
            {
                if (x < 0 || y < 0 || x >= texture.width || y >= texture.height)
                {
                    continue;
                }

                var dx = x - cx;
                var dy = y - cy;
                if (dx * dx + dy * dy <= radiusSq)
                {
                    texture.SetPixel(x, y, color);
                }
            }
        }
    }

    private void DrawLine(Texture2D texture, int x0, int y0, int x1, int y1, Color color, int thickness)
    {
        var dx = Mathf.Abs(x1 - x0);
        var dy = -Mathf.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;

        while (true)
        {
            DrawCircle(texture, x0, y0, Mathf.Max(1, thickness / 2), color);
            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            var e2 = 2 * error;
            if (e2 >= dy)
            {
                error += dy;
                x0 += sx;
            }
            if (e2 <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    private PlayerMove GetActiveCharacter()
    {
        var characters = FindObjectsByType<PlayerMove>(FindObjectsInactive.Include);
        return characters.FirstOrDefault(character => character != null && character.enabled && character.gameObject.activeInHierarchy)
            ?? characters.FirstOrDefault(character => character != null && character.gameObject.activeInHierarchy)
            ?? characters.FirstOrDefault(character => character != null);
    }

    private HandheldPose ResolveHandheldPose(PlayerMove character, string label, Vector3 defaultPosition, Vector3 defaultEuler, Vector3 defaultScale)
    {
        string key = GetCharacterItemKey(character, label);
        if (handheldPoseOverrides.TryGetValue(key, out var saved))
        {
            return new HandheldPose
            {
                label = label,
                localPosition = saved.localPosition,
                localEuler = saved.localEuler,
                localScale = saved.localScale
            };
        }

        if (handheldPoseOverrides.TryGetValue(label, out var sharedSaved))
        {
            return new HandheldPose
            {
                label = label,
                localPosition = sharedSaved.localPosition,
                localEuler = sharedSaved.localEuler,
                localScale = sharedSaved.localScale
            };
        }

        return new HandheldPose
        {
            label = label,
            localPosition = defaultPosition,
            localEuler = defaultEuler,
            localScale = defaultScale
        };
    }

    private void AttachToHand(PlayerMove character, GameObject item, HandheldPose pose, string label, Vector3 defaultPosition, Vector3 defaultEuler, Vector3 defaultScale)
    {
        if (character == null || item == null)
        {
            return;
        }

        var hand = FindHand(character.transform);
        if (hand == null)
        {
            Destroy(item);
            return;
        }

        ClearHeldItem(character);
        item.name = "StudioHeld_" + CleanLabel(item.name);
        var marker = item.GetComponent<StudioHeldItemMarker>() ?? item.AddComponent<StudioHeldItemMarker>();
        marker.label = string.IsNullOrWhiteSpace(label) ? CleanLabel(item.name) : label;
        marker.defaultLocalPosition = defaultPosition;
        marker.defaultLocalEuler = defaultEuler;
        marker.defaultLocalScale = defaultScale;

        item.transform.SetParent(hand, false);
        item.transform.localPosition = pose.localPosition;
        item.transform.localRotation = Quaternion.Euler(pose.localEuler);
        item.transform.localScale = pose.localScale;
        item.SetActive(true);
    }

    private StudioHeldItemMarker GetHeldItem(PlayerMove character)
    {
        if (character == null)
        {
            return null;
        }

        var hand = FindHand(character.transform);
        if (hand == null)
        {
            return null;
        }

        for (var i = hand.childCount - 1; i >= 0; i--)
        {
            var child = hand.GetChild(i);
            if (child == null)
            {
                continue;
            }

            var marker = child.GetComponent<StudioHeldItemMarker>();
            if (marker != null)
            {
                return marker;
            }
        }

        return null;
    }

    private void SaveHeldPose(StudioHeldItemMarker heldItem)
    {
        if (heldItem == null || string.IsNullOrWhiteSpace(heldItem.label))
        {
            return;
        }

        var activeCharacter = GetActiveCharacter();
        string key = GetCharacterItemKey(activeCharacter, heldItem.label);

        handheldPoseOverrides[key] = new HandheldPose
        {
            label = key,
            localPosition = heldItem.transform.localPosition,
            localEuler = NormalizeEuler(heldItem.transform.localEulerAngles),
            localScale = heldItem.transform.localScale
        };
        SaveHandheldPoseOverrides();
    }

    private void ResetHeldPose(StudioHeldItemMarker heldItem)
    {
        if (heldItem == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(heldItem.label))
        {
            var activeCharacter = GetActiveCharacter();
            handheldPoseOverrides.Remove(GetCharacterItemKey(activeCharacter, heldItem.label));
            SaveHandheldPoseOverrides();
        }

        heldItem.transform.localPosition = heldItem.defaultLocalPosition;
        heldItem.transform.localRotation = Quaternion.Euler(heldItem.defaultLocalEuler);
        heldItem.transform.localScale = heldItem.defaultLocalScale;
    }

    private void LoadHandheldPoseOverrides()
    {
        handheldPoseOverrides.Clear();
        var json = PlayerPrefs.GetString(HandheldPosePrefsKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            var collection = JsonUtility.FromJson<HandheldPoseCollection>(json);
            if (collection?.items == null)
            {
                return;
            }

            foreach (var item in collection.items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.label))
                {
                    continue;
                }

                handheldPoseOverrides[item.label] = item;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Could not load handheld pose overrides: " + ex.Message);
        }
    }

    private void SaveHandheldPoseOverrides()
    {
        var collection = new HandheldPoseCollection
        {
            items = handheldPoseOverrides.Values.ToArray()
        };
        PlayerPrefs.SetString(HandheldPosePrefsKey, JsonUtility.ToJson(collection));
        PlayerPrefs.Save();
    }

    private Vector3 NormalizeEuler(Vector3 euler)
    {
        euler.x = NormalizeAngle(euler.x);
        euler.y = NormalizeAngle(euler.y);
        euler.z = NormalizeAngle(euler.z);
        return euler;
    }

    private float NormalizeAngle(float value)
    {
        value %= 360f;
        if (value > 180f)
        {
            value -= 360f;
        }
        if (value < -180f)
        {
            value += 360f;
        }
        return value;
    }

    private void ClearHeldItem(PlayerMove character)
    {
        if (character == null)
        {
            return;
        }

        var hand = FindHand(character.transform);
        if (hand == null)
        {
            return;
        }

        for (var i = hand.childCount - 1; i >= 0; i--)
        {
            var child = hand.GetChild(i);
            if (child != null && child.name.StartsWith("StudioHeld_", StringComparison.OrdinalIgnoreCase))
            {
                Destroy(child.gameObject);
            }
        }
    }

    private Transform FindHand(Transform characterRoot)
    {
        if (characterRoot == null)
        {
            return null;
        }

        var animator = characterRoot.GetComponentInChildren<Animator>(true);
        if (animator != null && animator.isHuman)
        {
            var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (rightHand != null)
            {
                return rightHand;
            }
        }

        foreach (var child in characterRoot.GetComponentsInChildren<Transform>(true))
        {
            var normalized = Regex.Replace(child.name.ToLowerInvariant(), @"[^a-z0-9]", string.Empty);
            if (normalized.EndsWith("righthand", StringComparison.Ordinal)
                || normalized.EndsWith("handr", StringComparison.Ordinal)
                || normalized.EndsWith("rhand", StringComparison.Ordinal))
            {
                return child;
            }
        }

        return null;
    }

    private GameObject AddPrimitive(Transform parent, PrimitiveType type, string childName, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Color color, float metallic = 0f, float smoothness = 0.35f)
    {
        var primitive = GameObject.CreatePrimitive(type);
        primitive.name = childName;
        primitive.transform.SetParent(parent, false);
        primitive.transform.localPosition = localPosition;
        primitive.transform.localRotation = localRotation;
        primitive.transform.localScale = localScale;

        var renderer = primitive.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = CreateHeldMaterial(color, metallic, smoothness);
        }

        return primitive;
    }

    private Material CreateHeldMaterial(Color color, float metallic, float smoothness)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        var material = new Material(shader);
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", metallic);
        }
        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", smoothness);
        }
        return material;
    }

    private void PrepareHeldObject(GameObject item)
    {
        if (item == null)
        {
            return;
        }

        foreach (var collider in item.GetComponentsInChildren<Collider>(true))
        {
            Destroy(collider);
        }

        foreach (var body in item.GetComponentsInChildren<Rigidbody>(true))
        {
            Destroy(body);
        }

        foreach (var usable in item.GetComponentsInChildren<SimpleUsableProp>(true))
        {
            Destroy(usable);
        }

        foreach (var car in item.GetComponentsInChildren<SimpleDriveableCar>(true))
        {
            Destroy(car);
        }
    }

    private string MakeSpawnName(string label)
    {
        spawnedCounter++;
        return label + "_Spawned_" + spawnedCounter;
    }

    private Transform GetSpawnParent()
    {
        var parent = GameObject.Find("SpawnedInventoryProps");
        if (parent == null)
        {
            parent = new GameObject("SpawnedInventoryProps");
        }
        return parent.transform;
    }

    private Vector3 GetSpawnPoint()
    {
        var camera = Camera.main;
        if (camera != null)
        {
            var ray = camera.ScreenPointToRay(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            var plane = new Plane(Vector3.up, new Vector3(0f, moveGroundY, 0f));
            if (plane.Raycast(ray, out var enter))
            {
                return ray.GetPoint(enter);
            }
        }

        var active = FindObjectsByType<PlayerMove>(FindObjectsInactive.Exclude).FirstOrDefault(move => move.enabled);
        if (active != null)
        {
            return active.transform.position + active.transform.forward * 2f;
        }

        return new Vector3(0f, moveGroundY, 0f);
    }

    private void PrepareSpawnedObject(GameObject clone)
    {
        bool hasCollider = false;
        foreach (var collider in clone.GetComponentsInChildren<Collider>(true))
        {
            if (collider != null && clone.GetComponent<SimpleDriveableCar>() == null)
            {
                collider.isTrigger = true;
                hasCollider = true;
            }
        }

        if (!hasCollider)
        {
            var box = clone.AddComponent<BoxCollider>();
            box.isTrigger = true;
        }

        var lowerName = clone.name.ToLowerInvariant();
        if (lowerName.Contains("chair") || lowerName.Contains("sofa") || lowerName.Contains("bench") || lowerName.Contains("stool") || lowerName.Contains("toilet"))
        {
            AttachUsableProp(clone, SimpleUsableProp.UseMode.Sit);
        }
        else if (lowerName.Contains("bed") || lowerName.Contains("couch"))
        {
            AttachUsableProp(clone, SimpleUsableProp.UseMode.Lie);
        }
        else if (lowerName.Contains("bath"))
        {
            AttachUsableProp(clone, SimpleUsableProp.UseMode.Bath);
        }
    }

    private void FitSpawnedObjectToCharacterScale(GameObject clone, string label)
    {
        if (clone == null || !TryGetRendererBounds(clone.transform, out var bounds))
        {
            return;
        }

        var targetLargest = GetTargetLargestDimension(label);
        var currentLargest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (targetLargest <= 0.01f || currentLargest <= 0.01f)
        {
            return;
        }

        var multiplier = Mathf.Clamp(targetLargest / currentLargest, 0.01f, 100f);
        clone.transform.localScale *= multiplier;
    }

    private float GetTargetLargestDimension(string label)
    {
        var name = (label ?? string.Empty).ToLowerInvariant();
        if (name.Contains("bunk")) return 2.8f;
        if (name.Contains("bed")) return 2.45f;
        if (name.Contains("bathtub") || name.Contains("bath")) return 2.15f;
        if (name.Contains("shower")) return 2.05f;
        if (name.Contains("fridge")) return 1.95f;
        if (name.Contains("sofa") || name.Contains("couch")) return 2.25f;
        if (name.Contains("bench")) return 1.55f;
        if (name.Contains("chair") || name.Contains("stool")) return 1.15f;
        if (name.Contains("toilet")) return 1.05f;
        if (name.Contains("sink") || name.Contains("stove") || name.Contains("cabinet") || name.Contains("washer") || name.Contains("dryer")) return 1.25f;
        if (name.Contains("table") || name.Contains("desk")) return 1.65f;
        if (name.Contains("bookcase") || name.Contains("shelf")) return 1.95f;
        if (name.Contains("lamp")) return name.Contains("floor") ? 1.75f : 0.8f;
        if (name.Contains("rug")) return 2.25f;
        if (name.Contains("television") || name.Contains("screen")) return 1.15f;
        if (name.Contains("laptop") || name.Contains("keyboard") || name.Contains("mouse") || name.Contains("book") || name.Contains("pillow") || name.Contains("radio")) return 0.65f;
        if (name.Contains("plant") || name.Contains("bear") || name.Contains("box") || name.Contains("trashcan") || name.Contains("rack")) return 1.05f;
        return 1.25f;
    }

    private void AttachUsableProp(GameObject clone, SimpleUsableProp.UseMode mode)
    {
        if (clone.GetComponent<SimpleUsableProp>() != null) return;
        var usable = clone.AddComponent<SimpleUsableProp>();
        usable.mode = mode;
        
        var usePoint = new GameObject("UsePoint");
        usePoint.transform.SetParent(clone.transform, false);
        
        var renderers = clone.GetComponentsInChildren<Renderer>(true);
        Bounds b = default;
        bool found = false;
        for (var i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null || r is ParticleSystemRenderer) continue;

            var rBounds = r.localBounds;
            for (var corner = 0; corner < 8; corner++)
            {
                var localCorner = new Vector3(
                    (corner & 1) == 0 ? rBounds.min.x : rBounds.max.x,
                    (corner & 2) == 0 ? rBounds.min.y : rBounds.max.y,
                    (corner & 4) == 0 ? rBounds.min.z : rBounds.max.z);
                var rootLocalCorner = clone.transform.InverseTransformPoint(r.transform.TransformPoint(localCorner));

                if (!found)
                {
                    b = new Bounds(rootLocalCorner, Vector3.zero);
                    found = true;
                }
                else
                {
                    b.Encapsulate(rootLocalCorner);
                }
            }
        }

        if (found)
        {
            Vector3 localCenter = b.center;
            Vector3 localMin = b.min;
            Vector3 localMax = b.max;
            
            if (mode == SimpleUsableProp.UseMode.Sit)
            {
                usePoint.transform.localPosition = new Vector3(localCenter.x, localMin.y + (localMax.y - localMin.y) * 0.45f, localCenter.z);
            }
            else if (mode == SimpleUsableProp.UseMode.Lie)
            {
                var surfaceY = localMin.y + Mathf.Min((localMax.y - localMin.y) * 0.35f, 0.5f / Mathf.Max(0.001f, clone.transform.lossyScale.y));
                usePoint.transform.localPosition = new Vector3(localCenter.x, surfaceY, localCenter.z);
            }
            else if (mode == SimpleUsableProp.UseMode.Bath)
            {
                usePoint.transform.localPosition = new Vector3(localCenter.x, localMin.y + (localMax.y - localMin.y) * 0.42f, localCenter.z);
            }
            else
            {
                usePoint.transform.localPosition = new Vector3(localCenter.x, localCenter.y, localCenter.z);
            }

            var sizeX = (localMax.x - localMin.x) * clone.transform.lossyScale.x;
            var sizeZ = (localMax.z - localMin.z) * clone.transform.lossyScale.z;
            if (mode == SimpleUsableProp.UseMode.Lie)
            {
                usePoint.transform.localRotation = sizeX > sizeZ ? Quaternion.Euler(0f, 270f, 0f) : Quaternion.Euler(0f, 180f, 0f);
                usable.lieSurfaceOffset = -0.02f;
            }
        }
        usable.usePoint = usePoint.transform;

#if UNITY_EDITOR
        if (mode == SimpleUsableProp.UseMode.Sit)
        {
            usable.useAnimation = LoadFirstAnimationClip("Assets/DownloadedAnimations/Sitting Talking.fbx");
        }
        else if (mode == SimpleUsableProp.UseMode.Lie || mode == SimpleUsableProp.UseMode.Bath)
        {
            usable.useAnimation = LoadFirstAnimationClip("Assets/DownloadedAnimations/Male Laying Pose.fbx");
        }
#endif
    }

#if UNITY_EDITOR
    private AnimationClip LoadFirstAnimationClip(string path)
    {
        var assets = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
        for (var i = 0; i < assets.Length; i++)
        {
            if (assets[i] is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
            {
                return clip;
            }
        }

        return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
    }
#endif

    private void Select(Transform item)
    {
        selected = item;
        selectedScale = GetUniformScale(item);
    }

    private void ScaleSelected(float scale)
    {
        if (selected == null)
        {
            return;
        }

        selectedScale = Mathf.Clamp(scale, minScale, maxScale);
        selected.localScale = Vector3.one * selectedScale;
    }

    private void RotateSelected(float degrees)
    {
        if (selected == null)
        {
            return;
        }

        selected.Rotate(Vector3.up, degrees, Space.World);
    }

    private float GetUniformScale(Transform item)
    {
        var scale = item.localScale;
        return Mathf.Max((scale.x + scale.y + scale.z) / 3f, minScale);
    }

    private void RefreshAll()
    {
        nextRefreshTime = Time.unscaledTimeAsDouble + 0.5d;
        RefreshMovables();
        RefreshCatalog();
    }

    private void RefreshMovables()
    {
        movables.Clear();

        AddByName("Capsule");
        AddByName("Zoey");
        AddByName("MiraHead_NPC");
        AddByName("DriveableCar_GitHub");

        var furnitureParent = GameObject.Find("DownloadedFurnitureProps");
        if (furnitureParent != null)
        {
            foreach (Transform child in furnitureParent.transform)
            {
                AddMovable(child);
            }
        }

        var spawnedParent = GameObject.Find("SpawnedInventoryProps");
        if (spawnedParent != null)
        {
            foreach (Transform child in spawnedParent.transform)
            {
                AddMovable(child);
            }
        }

        foreach (var prop in FindObjectsByType<SimpleUsableProp>(FindObjectsInactive.Include))
        {
            AddMovable(GetSceneItemRoot(prop.transform));
        }

        foreach (var car in FindObjectsByType<SimpleDriveableCar>(FindObjectsInactive.Include))
        {
            AddMovable(GetSceneItemRoot(car.transform));
        }

        foreach (var move in FindObjectsByType<PlayerMove>(FindObjectsInactive.Include))
        {
            AddMovable(GetSceneItemRoot(move.transform));
        }

        if (selected != null && !movables.Contains(selected))
        {
            selected = null;
        }
    }

    private void RefreshCatalog()
    {
        catalog.Clear();
        var entriesByKey = new Dictionary<string, InventoryEntry>();

        AddCuratedCreateItems(entriesByKey);

        catalog.AddRange(entriesByKey.Values);
        catalog.Sort((a, b) => string.Compare(a.label, b.label, StringComparison.OrdinalIgnoreCase));
    }

    private void AddCatalogEntry(Dictionary<string, InventoryEntry> entriesByKey, string label, Transform template)
    {
        if (entriesByKey.Count >= TargetCatalogItems || template == null)
        {
            return;
        }

        var key = NormalizeCatalogKey(label);
        if (string.IsNullOrEmpty(key) || entriesByKey.ContainsKey(key) || IsExcludedCreateCatalogItem(key))
        {
            return;
        }

        var icon = ResolveIcon(label, template);
        if (!IsUsableCatalogIcon(icon))
        {
            return;
        }

        entriesByKey[key] = new InventoryEntry
        {
            label = FormatDisplayLabel(label),
            template = template,
            icon = icon
        };
    }

    private void AddCuratedCreateItems(Dictionary<string, InventoryEntry> entriesByKey)
    {
#if UNITY_EDITOR
        var car = GameObject.Find("DriveableCar_GitHub");
        if (car != null)
        {
            AddCatalogEntry(entriesByKey, "Driveable Car", car.transform);
        }

        foreach (var itemName in CuratedCreateItems)
        {
            var path = KenneyModelPath + itemName + ".fbx";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                continue;
            }

            AddCatalogEntry(entriesByKey, itemName, prefab.transform);
        }

        var modelGuids = AssetDatabase.FindAssets("t:Model", new[] { KenneyModelPath.TrimEnd('/') });
        for (var i = 0; i < modelGuids.Length; i++)
        {
            if (entriesByKey.Count >= TargetCatalogItems)
            {
                break;
            }

            var path = AssetDatabase.GUIDToAssetPath(modelGuids[i]);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var itemName = System.IO.Path.GetFileNameWithoutExtension(path);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                continue;
            }

            AddCatalogEntry(entriesByKey, itemName, prefab.transform);
        }
#endif
    }

    private void AddByName(string objectName)
    {
        var go = GameObject.Find(objectName);
        if (go != null)
        {
            AddMovable(go.transform);
        }
    }

    private void AddMovable(Transform item)
    {
        if (item == null || !item.gameObject.scene.IsValid() || IsIgnoredSceneItem(item) || movables.Contains(item))
        {
            return;
        }

        movables.Add(item);
    }

    private Transform GetSceneItemRoot(Transform item)
    {
        if (item == null)
        {
            return null;
        }

        var current = item;
        while (current.parent != null)
        {
            var parentName = current.parent.name;
            if (parentName == "DownloadedFurnitureProps" || parentName == "SpawnedInventoryProps")
            {
                return current;
            }

            if (current.parent.GetComponent<PlayerMove>() != null
                || current.parent.GetComponent<SimpleDriveableCar>() != null
                || current.parent.GetComponent<SimpleUsableProp>() != null)
            {
                current = current.parent;
                continue;
            }

            break;
        }

        return current;
    }

    private bool IsIgnoredSceneItem(Transform item)
    {
        if (item == null)
        {
            return true;
        }

        var name = item.name;
        if (name == "UsePoint"
            || name == "StudioBlockPreview"
            || name == "StudioBlocks"
            || name == "DownloadedFurnitureProps"
            || name == "SpawnedInventoryProps")
        {
            return true;
        }

        if (item.GetComponentInParent<StudioBuildBlock>() != null && item.name != "StudioBlocks")
        {
            return true;
        }

        return false;
    }

    private string NormalizeCatalogKey(string itemName)
    {
        var label = CleanLabel(itemName).ToLowerInvariant();
        label = Regex.Replace(label, @"_spawned_\d+$", string.Empty, RegexOptions.IgnoreCase);
        label = Regex.Replace(label, @"\s*\(\d+\)$", string.Empty, RegexOptions.IgnoreCase);
        label = Regex.Replace(label, @"[_\-\s]+copy$", string.Empty, RegexOptions.IgnoreCase);
        label = Regex.Replace(label, @"[_\-\s]+\d+$", string.Empty, RegexOptions.IgnoreCase);
        label = Regex.Replace(label, @"[^a-z0-9]+", "_", RegexOptions.IgnoreCase).Trim('_');
        return label;
    }

    private bool IsExcludedCreateCatalogItem(string normalizedKey)
    {
        if (string.IsNullOrEmpty(normalizedKey))
        {
            return true;
        }

        return normalizedKey.StartsWith("floor", StringComparison.OrdinalIgnoreCase)
            || normalizedKey.StartsWith("wall", StringComparison.OrdinalIgnoreCase)
            || normalizedKey.StartsWith("door", StringComparison.OrdinalIgnoreCase)
            || normalizedKey.StartsWith("doorway", StringComparison.OrdinalIgnoreCase)
            || normalizedKey.StartsWith("window", StringComparison.OrdinalIgnoreCase)
            || normalizedKey.Contains("_wall")
            || normalizedKey.Contains("_floor");
    }

    private string CleanLabel(string itemName)
    {
        var label = itemName.Replace("(Clone)", string.Empty).Trim();
        label = Regex.Replace(label, @"_Spawned_\d+$", string.Empty, RegexOptions.IgnoreCase);
        label = Regex.Replace(label, @"\s*\(\d+\)$", string.Empty, RegexOptions.IgnoreCase);
        return label.Trim();
    }

    private string FormatDisplayLabel(string itemName)
    {
        var label = CleanLabel(itemName).Replace("_", " ").Replace("-", " ");
        label = Regex.Replace(label, @"(?<=[a-z0-9])(?=[A-Z])", " ");
        label = Regex.Replace(label, @"\s+", " ").Trim();
        if (string.IsNullOrEmpty(label))
        {
            return itemName;
        }

        return char.ToUpperInvariant(label[0]) + label.Substring(1);
    }

    private Texture2D ResolveIcon(string itemName, Transform template)
    {
        var key = NormalizeCatalogKey(itemName);
        if (iconCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var icon = LoadIconForName(itemName, template);
        if (IsUsableCatalogIcon(icon))
        {
            iconCache[key] = icon;
        }
        return icon;
    }

    private Texture2D LoadIconForName(string itemName, Transform template)
    {
#if UNITY_EDITOR
        var exactTopdownPath = GuessExactKenneyIconPath(itemName);
        if (!string.IsNullOrEmpty(exactTopdownPath))
        {
            var exactTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(exactTopdownPath);
            if (exactTexture != null)
            {
                return exactTexture;
            }
        }

        var path = GuessIconPath(itemName);
        if (!string.IsNullOrEmpty(path))
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture != null)
            {
                return texture;
            }
        }

        if (template != null)
        {
            var preview = AssetPreview.GetAssetPreview(template.gameObject);
            if (preview != null)
            {
                return preview;
            }
        }
#endif
        return null;
    }

    private bool IsUsableCatalogIcon(Texture2D icon)
    {
        return icon != null && icon != Texture2D.whiteTexture;
    }

    private string GuessIconPath(string itemName)
    {
        var name = itemName.ToLowerInvariant();

        if (name.Contains("car")) return "Assets/DownloadedProps/SimpleCarController/ARCADE - FREE Racing Car/Textures/Color Variations/AFRC_Tex_Col5.png";
        if (name.Contains("bed")) return KenneyTopdownPath + "bedDouble.png";
        if (name.Contains("bathtub") || name.Contains("bath")) return KenneyTopdownPath + "bathtub.png";
        if (name.Contains("toilet")) return KenneyTopdownPath + "toilet.png";
        if (name.Contains("shower")) return KenneyTopdownPath + "shower.png";
        if (name.Contains("sofa")) return KenneyTopdownPath + "loungeSofa.png";
        if (name.Contains("chair")) return KenneyTopdownPath + "chair.png";
        if (name.Contains("fridge")) return KenneyTopdownPath + "kitchenFridge.png";
        if (name.Contains("stove")) return KenneyTopdownPath + "kitchenStove.png";
        if (name.Contains("tv") || name.Contains("television")) return KenneyTopdownPath + "televisionModern.png";
        if (name.Contains("table")) return KenneyTopdownPath + "table.png";
        if (name.Contains("lamp")) return KenneyTopdownPath + "lampRoundFloor.png";
        if (name.Contains("bookcase")) return KenneyTopdownPath + "bookcaseOpen.png";
        return null;
    }

    private string GuessExactKenneyIconPath(string itemName)
    {
#if UNITY_EDITOR
        var clean = CleanLabel(itemName);
        var compact = Regex.Replace(clean, @"[^a-zA-Z0-9]", string.Empty);
        if (string.IsNullOrEmpty(compact))
        {
            return null;
        }

        var exact = KenneyTopdownPath + compact + ".png";
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(exact) != null)
        {
            return exact;
        }
#endif
        return null;
    }

    private string GetCharacterItemKey(PlayerMove character, string itemLabel)
    {
        if (character == null) return itemLabel;
        return CleanLabel(character.name) + "_" + itemLabel;
    }

    private void DrawRecordingsTab()
    {
        var recorder = CharacterActionRecorder.Instance;
        if (recorder == null)
        {
            GUILayout.Label("Recording system is not initialized.", labelStyle);
            return;
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("Recordings Manager", headerStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label("Mode: " + recorder.CurrentMode.ToString().ToUpperInvariant(), mutedLabelStyle);
        GUILayout.EndHorizontal();
        GUILayout.Space(8f);

        GUILayout.BeginVertical(panelStyle);
        GUILayout.BeginHorizontal();
        
        bool isRec = recorder.CurrentMode == CharacterActionRecorder.Mode.Recording;
        bool isPlay = recorder.CurrentMode == CharacterActionRecorder.Mode.Playing;
        bool isStandby = recorder.CurrentMode == CharacterActionRecorder.Mode.Standby;
        bool isRendering = CharacterActionRecorder.IsRenderingVideo;

        GUI.enabled = !isRendering && (isStandby || isRec);
        if (GUILayout.Button(isRec ? "Stop Record" : "Record Active", isRec ? activeButtonStyle : buttonStyle, GUILayout.Height(30f)))
        {
            recorder.ToggleRecording();
        }

        GUI.enabled = !isRendering && (isStandby && recorder.GetReadyCount() > 0 || isPlay);
        if (GUILayout.Button(isPlay ? "Stop Playback" : "Play All", isPlay ? activeButtonStyle : buttonStyle, GUILayout.Height(30f)))
        {
            recorder.TogglePlayback();
        }

        GUI.enabled = !isStandby || isRendering;
        if (GUILayout.Button("Stop All", activeButtonStyle, GUILayout.Height(30f)))
        {
            recorder.StopCurrentOperation();
        }

        GUI.enabled = !isRendering && isStandby && recorder.RecordingsList.Count > 0;
        if (GUILayout.Button("Reset", buttonStyle, GUILayout.Height(30f)))
        {
            recorder.ResetAll();
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        GUILayout.Space(10f);

        // Save Take section
        GUILayout.BeginVertical(panelStyle);
        GUILayout.Label("Save Current Take", headerStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Name", labelStyle, GUILayout.Width(42f));
        recorder.TakeNameField = GUILayout.TextField(recorder.TakeNameField, GUILayout.Height(22f));
        GUI.enabled = recorder.GetReadyCount() > 0;
        if (GUILayout.Button("Save Take", activeButtonStyle, GUILayout.Width(92f), GUILayout.Height(22f)))
        {
            recorder.PublicSaveCurrentTake();
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        GUILayout.Space(10f);

        // List memory/saved takes
        recsScroll = GUILayout.BeginScrollView(recsScroll, GUILayout.Height(170f));
        
        // In-Memory List
        GUILayout.Label("In-Memory Recordings (" + recorder.RecordingsList.Count + ")", headerStyle);
        if (recorder.RecordingsList.Count == 0)
        {
            GUILayout.Label("  No temporary recordings.", mutedLabelStyle);
        }
        else
        {
            for (int i = 0; i < recorder.RecordingsList.Count; i++)
            {
                var rec = recorder.RecordingsList[i];
                if (rec == null) continue;
                GUILayout.BeginHorizontal(panelStyle);
                string charName = rec.character != null ? rec.character.name : "Unknown Character";
                GUILayout.Label("• " + CleanLabel(charName), labelStyle);
                GUILayout.FlexibleSpace();
                GUILayout.Label(rec.duration.ToString("F1") + "s (" + rec.frames.Count + " f)", mutedLabelStyle);
                GUILayout.Space(10f);
                GUI.enabled = isStandby;
                if (GUILayout.Button("Delete", buttonStyle, GUILayout.Width(62f), GUILayout.Height(20f)))
                {
                    recorder.PublicDeleteMemoryRecording(i);
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        GUILayout.Space(10f);

        // Saved Takes List
        GUILayout.Label("Saved Takes (" + recorder.SavedTakesList.Length + ")", headerStyle);
        if (recorder.SavedTakesList.Length == 0)
        {
            GUILayout.Label("  No saved takes.", mutedLabelStyle);
        }
        else
        {
            for (int i = 0; i < recorder.SavedTakesList.Length; i++)
            {
                string path = recorder.SavedTakesList[i];
                string filename = System.IO.Path.GetFileNameWithoutExtension(path);
                GUILayout.BeginHorizontal(panelStyle);
                GUILayout.Label("📁 " + filename, labelStyle);
                GUILayout.FlexibleSpace();
                GUI.enabled = isStandby;
                if (GUILayout.Button("Load", buttonStyle, GUILayout.Width(50f), GUILayout.Height(20f)))
                {
                    recorder.PublicLoadTake(path);
                }
                if (GUILayout.Button("Render", buttonStyle, GUILayout.Width(60f), GUILayout.Height(20f)))
                {
                    recorder.PublicRenderTake(path);
                }
                if (GUILayout.Button("Delete", buttonStyle, GUILayout.Width(60f), GUILayout.Height(20f)))
                {
                    recorder.PublicDeleteTake(path);
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        GUILayout.EndScrollView();

        GUILayout.Space(10f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Open Folder", buttonStyle, GUILayout.Height(26f)))
        {
            recorder.PublicOpenRecordingsFolder();
        }
        if (GUILayout.Button("Open Videos", buttonStyle, GUILayout.Height(26f)))
        {
            recorder.PublicOpenVideosFolder();
        }
        GUILayout.EndHorizontal();
        
        GUILayout.Label(recorder.StatusMessageText, mutedLabelStyle);
    }

    private static void SetOpen(bool open)
    {
        IsOpen = open;
        if (open)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (SurfaceItemPlacer.IsPlacing)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }
}

public class StudioHeldItemMarker : MonoBehaviour
{
    public string label;
    public Vector3 defaultLocalPosition;
    public Vector3 defaultLocalEuler;
    public Vector3 defaultLocalScale = Vector3.one;
}



