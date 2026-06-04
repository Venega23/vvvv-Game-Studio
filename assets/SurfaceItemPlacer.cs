using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class SurfaceItemPlacer : MonoBehaviour
{
    public static SurfaceItemPlacer Instance { get; private set; }
    public static bool IsPlacing => Instance != null && Instance.heldObject != null;
    public static bool IsDragging => Instance != null && Instance.isDragging;
    public static bool IsPaletteOpen => Instance != null && Instance.itemModeActive;
    public static bool BlocksGameplayInput => IsDragging;
    public static bool BlocksToolInput => IsPlacing || IsPaletteOpen;

    public Key paletteKey = Key.B;
    public Key pickupKey = Key.G;
    public Key cancelKey = Key.X;
    public Key rotateLeftKey = Key.Q;
    public Key rotateRightKey = Key.E;
    public float pickupDistance = 5f;
    public float placeDistance = 12f;
    public float rotateDegreesPerScroll = 12f;
    public float rotateDegreesPerSecond = 120f;
    public float normalOffsetStep = 0.025f;
    public float maxNormalOffset = 1f;

    private Transform heldObject;
    private Transform originalParent;
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private Vector3 originalScale;
    private float yaw;
    private float normalOffset;
    private bool isDragging;
    private bool hasValidSurface;
    private bool itemModeActive;
    private int selectedPaletteIndex;
    private RaycastHit lastSurfaceHit;
    private Collider[] heldColliders = Array.Empty<Collider>();
    private Rigidbody[] heldRigidbodies = Array.Empty<Rigidbody>();
    private bool[] colliderEnabled = Array.Empty<bool>();
    private bool[] colliderTriggers = Array.Empty<bool>();
    private bool[] bodyKinematic = Array.Empty<bool>();
    private bool[] bodyGravity = Array.Empty<bool>();
    private readonly List<RendererState> rendererStates = new();
    private Material previewMaterial;
    private GUIStyle hudStyle;
    private GUIStyle hudShadowStyle;
    private GUIStyle paletteBoxStyle;
    private GUIStyle paletteItemStyle;
    private GUIStyle paletteSelectedStyle;
    private readonly List<PaletteItem> palette = new();

    private enum PlaceableShape
    {
        Vase,
        Microphone,
        Phone,
        Book,
        Apple,
        Cup,
        Bottle,
        Laptop,
        TableLamp,
        Plant,
        Remote,
        Camera,
        Tablet,
        Notebook,
        Keyring,
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

    private class PaletteItem
    {
        public string label;
        public PlaceableShape shape;
        public Color color;
        public float scale = 1f;
    }

    private struct RendererState
    {
        public Renderer renderer;
        public Material[] materials;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (FindAnyObjectByType<SurfaceItemPlacer>() != null)
        {
            return;
        }

        var go = new GameObject("SurfaceItemPlacer");
        DontDestroyOnLoad(go);
        go.AddComponent<SurfaceItemPlacer>();
    }

    private void Awake()
    {
        Instance = this;
        BuildPalette();
        EnsurePreviewMaterial();
    }

    private void OnDestroy()
    {
        if (heldObject != null)
        {
            CancelPlacement();
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;
        if (keyboard == null || mouse == null || SimpleInventoryWindow.IsOpen || MapSwitcher.IsMenuOpen || CharacterActionRecorder.IsMenuOpen || CharacterTuningMenu.IsOpen)
        {
            isDragging = false;
            return;
        }

        if (keyboard[paletteKey].wasPressedThisFrame)
        {
            itemModeActive = !itemModeActive;
            if (itemModeActive)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                BlockBuildMode.IsBuildMode = false;
            }
            else if (heldObject != null)
            {
                CancelPlacement();
            }
            return;
        }

        if (heldObject == null)
        {
            if (itemModeActive)
            {
                HandleItemModeInput(mouse, keyboard);
                return;
            }

            if (keyboard[pickupKey].wasPressedThisFrame)
            {
                TryPickupLookedObject();
                return;
            }

            if (mouse.rightButton.wasPressedThisFrame && TryPickupLookedObject())
            {
                return;
            }
            return;
        }

        if (keyboard[cancelKey].wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame)
        {
            CancelPlacement();
            return;
        }

        HandleScroll(mouse, keyboard);

        if (keyboard[rotateLeftKey].isPressed)
        {
            yaw -= rotateDegreesPerSecond * Time.deltaTime;
        }
        if (keyboard[rotateRightKey].isPressed)
        {
            yaw += rotateDegreesPerSecond * Time.deltaTime;
        }

        if (keyboard.eKey.isPressed)
        {
            var deltaX = mouse.delta.ReadValue().x;
            if (Mathf.Abs(deltaX) > 0.01f)
            {
                yaw += deltaX * 0.35f;
            }
        }

        UpdateHeldPreview(mouse);

        if (mouse.rightButton.wasPressedThisFrame)
        {
            PlaceHeldObject();
        }
    }

    private void OnGUI()
    {
        if (heldObject == null)
        {
            if (itemModeActive)
            {
                DrawPalette();
                DrawPlacementCrosshair(true);
            }
            return;
        }

        InitHudStyles();
        var rect = new Rect(0f, Screen.height - 78f, Screen.width, 64f);
        var text = hasValidSurface
            ? "B: items | G: pickup | RMB: place | Q/E: rotate | hold E + mouse: fine rotate | wheel: scale | Shift+wheel: height | X/Esc: cancel"
            : "Aim at a surface to place this item";
        GUI.Label(new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height), text, hudShadowStyle);
        GUI.Label(rect, text, hudStyle);

        DrawPlacementCrosshair(hasValidSurface);
    }

    private bool TryPickupLookedObject()
    {
        var camera = Camera.main;
        if (camera == null)
        {
            return false;
        }

        return TryPickupAtScreenPoint(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
    }

    private bool TryPickupAtScreenPoint(Vector2 screenPoint)
    {
        var camera = Camera.main;
        if (camera == null)
        {
            return false;
        }

        var ray = camera.ScreenPointToRay(screenPoint);
        if (!TryGetBestHit(ray, pickupDistance, null, out var hit))
        {
            return false;
        }

        var root = FindPlaceableRoot(hit.transform);
        if (root == null)
        {
            return false;
        }

        BeginPlacement(root);
        return true;
    }

    private void HandleItemModeInput(Mouse mouse, Keyboard keyboard)
    {
        if (keyboard.escapeKey.wasPressedThisFrame || keyboard[cancelKey].wasPressedThisFrame)
        {
            itemModeActive = false;
            return;
        }

        var scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.1f && palette.Count > 0)
        {
            var direction = scroll > 0f ? -1 : 1;
            selectedPaletteIndex = (selectedPaletteIndex + direction + palette.Count) % palette.Count;
        }

        if (mouse.rightButton.wasPressedThisFrame)
        {
            SpawnSelectedPaletteItem();
            return;
        }

        if (mouse.leftButton.wasPressedThisFrame && TryPickupAtScreenPoint(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)))
        {
            isDragging = true;
        }
    }

    private void SpawnSelectedPaletteItem()
    {
        if (palette.Count == 0)
        {
            return;
        }

        selectedPaletteIndex = Mathf.Clamp(selectedPaletteIndex, 0, palette.Count - 1);
        var item = palette[selectedPaletteIndex];
        var root = CreatePaletteObject(item);
        StartPlacement(root.transform);
    }

    public void StartPlacement(Transform item)
    {
        if (item == null)
        {
            return;
        }

        if (heldObject != null)
        {
            CancelPlacement();
        }

        BeginPlacement(item);
    }

    private void BeginPlacement(Transform item)
    {
        heldObject = item;
        originalParent = item.parent;
        originalPosition = item.position;
        originalRotation = item.rotation;
        originalScale = item.localScale;
        yaw = item.eulerAngles.y;
        normalOffset = 0f;
        hasValidSurface = false;

        BlockBuildMode.IsBuildMode = false;
        LockGameplayCursor();
        CacheAndDisablePhysics(item);
        ApplyPreviewMaterial(item);
    }

    private void UpdateHeldPreview(Mouse mouse)
    {
        var camera = Camera.main;
        if (camera == null)
        {
            hasValidSurface = false;
            return;
        }

        isDragging = mouse.leftButton.isPressed;
        if (isDragging && Cursor.lockState == CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (!isDragging && Cursor.lockState != CursorLockMode.Locked)
        {
            LockGameplayCursor();
        }

        var screenPoint = isDragging
            ? mouse.position.ReadValue()
            : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        var ray = camera.ScreenPointToRay(screenPoint);
        hasValidSurface = TryGetBestHit(ray, placeDistance, heldObject, out lastSurfaceHit);
        if (!hasValidSurface)
        {
            heldObject.position = camera.transform.position + camera.transform.forward * Mathf.Min(3f, placeDistance);
            heldObject.rotation = Quaternion.Euler(0f, yaw, 0f);
            return;
        }

        heldObject.rotation = Quaternion.Euler(0f, yaw, 0f);
        heldObject.position = lastSurfaceHit.point;

        var bounds = GetRendererBounds(heldObject);
        var normal = lastSurfaceHit.normal.sqrMagnitude > 0.001f ? lastSurfaceHit.normal.normalized : Vector3.up;
        var extent = GetProjectedExtent(bounds.extents, normal);
        var targetCenter = lastSurfaceHit.point + normal * (extent + normalOffset);
        heldObject.position += targetCenter - bounds.center;
    }

    private void PlaceHeldObject()
    {
        if (heldObject == null)
        {
            return;
        }

        if (!hasValidSurface)
        {
            return;
        }

        RestoreMaterials();
        RestorePhysics();
        heldObject.SetParent(originalParent, true);
        heldObject = null;
        isDragging = false;
        hasValidSurface = false;
        itemModeActive = false;
        LockGameplayCursor();
    }

    private void CancelPlacement()
    {
        if (heldObject == null)
        {
            return;
        }

        RestoreMaterials();
        RestorePhysics();
        heldObject.SetParent(originalParent, true);
        heldObject.SetPositionAndRotation(originalPosition, originalRotation);
        heldObject.localScale = originalScale;
        heldObject = null;
        isDragging = false;
        hasValidSurface = false;
        itemModeActive = false;
        LockGameplayCursor();
    }

    private void LockGameplayCursor()
    {
        if (SimpleInventoryWindow.IsOpen || MapSwitcher.IsMenuOpen || CharacterActionRecorder.IsMenuOpen || CharacterTuningMenu.IsOpen)
        {
            return;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void BuildPalette()
    {
        palette.Clear();
        palette.Add(new PaletteItem { label = "Vase", shape = PlaceableShape.Vase, color = new Color(0.78f, 0.32f, 0.22f), scale = 0.55f });
        palette.Add(new PaletteItem { label = "Microphone", shape = PlaceableShape.Microphone, color = new Color(0.08f, 0.08f, 0.09f), scale = 0.52f });
        palette.Add(new PaletteItem { label = "Phone", shape = PlaceableShape.Phone, color = new Color(0.025f, 0.03f, 0.04f), scale = 0.45f });
        palette.Add(new PaletteItem { label = "Book", shape = PlaceableShape.Book, color = new Color(0.62f, 0.08f, 0.11f), scale = 0.58f });
        palette.Add(new PaletteItem { label = "Apple", shape = PlaceableShape.Apple, color = new Color(0.78f, 0.05f, 0.04f), scale = 0.34f });
        palette.Add(new PaletteItem { label = "Cup", shape = PlaceableShape.Cup, color = new Color(0.96f, 0.92f, 0.78f), scale = 0.42f });
        palette.Add(new PaletteItem { label = "Bottle", shape = PlaceableShape.Bottle, color = new Color(0.12f, 0.55f, 0.88f), scale = 0.48f });
        palette.Add(new PaletteItem { label = "Laptop", shape = PlaceableShape.Laptop, color = new Color(0.13f, 0.14f, 0.16f), scale = 0.68f });
        palette.Add(new PaletteItem { label = "Lamp", shape = PlaceableShape.TableLamp, color = new Color(0.95f, 0.82f, 0.52f), scale = 0.58f });
        palette.Add(new PaletteItem { label = "Plant", shape = PlaceableShape.Plant, color = new Color(0.16f, 0.55f, 0.22f), scale = 0.62f });
        palette.Add(new PaletteItem { label = "Remote", shape = PlaceableShape.Remote, color = new Color(0.04f, 0.045f, 0.05f), scale = 0.42f });
        palette.Add(new PaletteItem { label = "Camera", shape = PlaceableShape.Camera, color = new Color(0.07f, 0.075f, 0.085f), scale = 0.55f });
        palette.Add(new PaletteItem { label = "Tablet", shape = PlaceableShape.Tablet, color = new Color(0.025f, 0.03f, 0.04f), scale = 0.55f });
        palette.Add(new PaletteItem { label = "Notebook", shape = PlaceableShape.Notebook, color = new Color(0.16f, 0.34f, 0.74f), scale = 0.50f });
        palette.Add(new PaletteItem { label = "Keys", shape = PlaceableShape.Keyring, color = new Color(0.95f, 0.74f, 0.22f), scale = 0.38f });
        palette.Add(new PaletteItem { label = "Soda can", shape = PlaceableShape.SodaCan, color = new Color(0.82f, 0.06f, 0.08f), scale = 0.38f });
        palette.Add(new PaletteItem { label = "Pizza", shape = PlaceableShape.PizzaSlice, color = new Color(0.95f, 0.62f, 0.22f), scale = 0.48f });
        palette.Add(new PaletteItem { label = "Donut", shape = PlaceableShape.Donut, color = new Color(0.72f, 0.38f, 0.18f), scale = 0.42f });
        palette.Add(new PaletteItem { label = "Gamepad", shape = PlaceableShape.GameController, color = new Color(0.04f, 0.045f, 0.055f), scale = 0.48f });
        palette.Add(new PaletteItem { label = "Headphones", shape = PlaceableShape.Headphones, color = new Color(0.06f, 0.065f, 0.075f), scale = 0.52f });
        palette.Add(new PaletteItem { label = "Candle", shape = PlaceableShape.Candle, color = new Color(0.98f, 0.86f, 0.55f), scale = 0.42f });
        palette.Add(new PaletteItem { label = "Frame", shape = PlaceableShape.PictureFrame, color = new Color(0.56f, 0.36f, 0.18f), scale = 0.56f });
        palette.Add(new PaletteItem { label = "Bowl", shape = PlaceableShape.Bowl, color = new Color(0.94f, 0.90f, 0.82f), scale = 0.44f });
        palette.Add(new PaletteItem { label = "Spoon", shape = PlaceableShape.Spoon, color = new Color(0.76f, 0.78f, 0.80f), scale = 0.42f });
        palette.Add(new PaletteItem { label = "Toothbrush", shape = PlaceableShape.Toothbrush, color = new Color(0.08f, 0.62f, 0.82f), scale = 0.38f });
        palette.Add(new PaletteItem { label = "Pen", shape = PlaceableShape.Pen, color = new Color(0.02f, 0.025f, 0.03f), scale = 0.36f });
    }

    private GameObject CreatePaletteObject(PaletteItem item)
    {
        var root = new GameObject("Placeable " + item.label);
        root.transform.SetParent(GetSpawnParent(), true);
        root.transform.position = GetFallbackSpawnPoint();
        root.transform.localScale = Vector3.one * item.scale;

        switch (item.shape)
        {
            case PlaceableShape.Vase:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Body", new Vector3(0f, 0.38f, 0f), Quaternion.identity, new Vector3(0.52f, 0.68f, 0.52f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Belly", new Vector3(0f, 0.34f, 0f), Quaternion.identity, new Vector3(0.72f, 0.46f, 0.72f), item.color * 0.92f);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Neck", new Vector3(0f, 0.92f, 0f), Quaternion.identity, new Vector3(0.28f, 0.34f, 0.28f), item.color * 1.08f);
                break;
            case PlaceableShape.Microphone:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Handle", new Vector3(0f, 0.34f, 0f), Quaternion.identity, new Vector3(0.16f, 0.68f, 0.16f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Head", new Vector3(0f, 0.98f, 0f), Quaternion.identity, new Vector3(0.42f, 0.36f, 0.42f), new Color(0.16f, 0.16f, 0.17f));
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Ring", new Vector3(0f, 0.70f, 0f), Quaternion.identity, new Vector3(0.22f, 0.05f, 0.22f), new Color(0.72f, 0.74f, 0.76f));
                break;
            case PlaceableShape.Phone:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Body", new Vector3(0f, 0.04f, 0f), Quaternion.identity, new Vector3(0.55f, 0.08f, 0.95f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Screen", new Vector3(0f, 0.086f, 0f), Quaternion.identity, new Vector3(0.45f, 0.018f, 0.76f), new Color(0.03f, 0.42f, 0.58f));
                break;
            case PlaceableShape.Book:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Cover", new Vector3(0f, 0.055f, 0f), Quaternion.identity, new Vector3(0.82f, 0.10f, 1.08f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Pages", new Vector3(0.08f, 0.115f, 0f), Quaternion.identity, new Vector3(0.62f, 0.035f, 0.88f), new Color(0.92f, 0.86f, 0.68f));
                break;
            case PlaceableShape.Apple:
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Body", new Vector3(0f, 0.28f, 0f), Quaternion.identity, new Vector3(0.58f, 0.52f, 0.58f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Stem", new Vector3(0f, 0.64f, 0f), Quaternion.Euler(12f, 0f, -18f), new Vector3(0.08f, 0.22f, 0.08f), new Color(0.34f, 0.18f, 0.08f));
                AddPrimitive(root.transform, PrimitiveType.Cube, "Leaf", new Vector3(0.18f, 0.74f, 0f), Quaternion.Euler(0f, 0f, 28f), new Vector3(0.28f, 0.04f, 0.13f), new Color(0.18f, 0.56f, 0.22f));
                break;
            case PlaceableShape.Cup:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Cup", new Vector3(0f, 0.32f, 0f), Quaternion.identity, new Vector3(0.46f, 0.58f, 0.46f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Handle", new Vector3(0.42f, 0.30f, 0f), Quaternion.identity, new Vector3(0.12f, 0.32f, 0.11f), item.color);
                break;
            case PlaceableShape.Bottle:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Body", new Vector3(0f, 0.42f, 0f), Quaternion.identity, new Vector3(0.34f, 0.78f, 0.34f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Neck", new Vector3(0f, 0.98f, 0f), Quaternion.identity, new Vector3(0.18f, 0.28f, 0.18f), item.color * 0.9f);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Cap", new Vector3(0f, 1.18f, 0f), Quaternion.identity, new Vector3(0.20f, 0.10f, 0.20f), new Color(0.05f, 0.07f, 0.1f));
                break;
            case PlaceableShape.Laptop:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Base", new Vector3(0f, 0.045f, 0f), Quaternion.identity, new Vector3(1.15f, 0.08f, 0.78f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Screen", new Vector3(0f, 0.45f, 0.38f), Quaternion.Euler(-72f, 0f, 0f), new Vector3(1.10f, 0.06f, 0.70f), new Color(0.035f, 0.04f, 0.05f));
                AddPrimitive(root.transform, PrimitiveType.Cube, "Display", new Vector3(0f, 0.47f, 0.35f), Quaternion.Euler(-72f, 0f, 0f), new Vector3(0.92f, 0.02f, 0.52f), new Color(0.04f, 0.36f, 0.55f));
                break;
            case PlaceableShape.TableLamp:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Base", new Vector3(0f, 0.06f, 0f), Quaternion.identity, new Vector3(0.48f, 0.08f, 0.48f), new Color(0.34f, 0.28f, 0.20f));
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Pole", new Vector3(0f, 0.48f, 0f), Quaternion.identity, new Vector3(0.08f, 0.72f, 0.08f), new Color(0.45f, 0.38f, 0.28f));
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Shade", new Vector3(0f, 0.98f, 0f), Quaternion.identity, new Vector3(0.62f, 0.38f, 0.62f), item.color);
                break;
            case PlaceableShape.Plant:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Pot", new Vector3(0f, 0.20f, 0f), Quaternion.identity, new Vector3(0.48f, 0.36f, 0.48f), new Color(0.46f, 0.22f, 0.12f));
                AddPrimitive(root.transform, PrimitiveType.Sphere, "LeavesA", new Vector3(-0.10f, 0.62f, 0f), Quaternion.identity, new Vector3(0.50f, 0.36f, 0.50f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "LeavesB", new Vector3(0.16f, 0.76f, 0.05f), Quaternion.identity, new Vector3(0.44f, 0.32f, 0.44f), item.color * 1.14f);
                break;
            case PlaceableShape.Remote:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Body", new Vector3(0f, 0.035f, 0f), Quaternion.identity, new Vector3(0.25f, 0.07f, 0.95f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cube, "ButtonA", new Vector3(0f, 0.08f, 0.20f), Quaternion.identity, new Vector3(0.12f, 0.02f, 0.12f), new Color(0.65f, 0.05f, 0.05f));
                AddPrimitive(root.transform, PrimitiveType.Cube, "ButtonB", new Vector3(0f, 0.08f, -0.05f), Quaternion.identity, new Vector3(0.13f, 0.02f, 0.18f), new Color(0.20f, 0.22f, 0.24f));
                break;
            case PlaceableShape.Camera:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Body", new Vector3(0f, 0.28f, 0f), Quaternion.identity, new Vector3(0.76f, 0.42f, 0.34f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Lens", new Vector3(0f, 0.28f, -0.28f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.28f, 0.25f, 0.28f), new Color(0.02f, 0.025f, 0.03f));
                AddPrimitive(root.transform, PrimitiveType.Cube, "Top", new Vector3(-0.18f, 0.54f, 0f), Quaternion.identity, new Vector3(0.26f, 0.12f, 0.24f), item.color * 1.2f);
                break;
            case PlaceableShape.Tablet:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Body", new Vector3(0f, 0.045f, 0f), Quaternion.identity, new Vector3(0.78f, 0.08f, 1.14f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Screen", new Vector3(0f, 0.092f, 0f), Quaternion.identity, new Vector3(0.64f, 0.018f, 0.94f), new Color(0.03f, 0.34f, 0.52f));
                break;
            case PlaceableShape.Notebook:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Cover", new Vector3(0f, 0.045f, 0f), Quaternion.identity, new Vector3(0.72f, 0.08f, 0.96f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Pages", new Vector3(0.04f, 0.09f, 0f), Quaternion.identity, new Vector3(0.58f, 0.035f, 0.82f), new Color(0.93f, 0.91f, 0.82f));
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Spine", new Vector3(-0.36f, 0.12f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.04f, 0.96f, 0.04f), item.color * 0.75f);
                break;
            case PlaceableShape.Keyring:
                AddPrimitive(root.transform, PrimitiveType.Sphere, "RingA", new Vector3(-0.10f, 0.08f, 0f), Quaternion.identity, new Vector3(0.16f, 0.035f, 0.16f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "RingB", new Vector3(0.10f, 0.08f, 0f), Quaternion.identity, new Vector3(0.16f, 0.035f, 0.16f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cube, "KeyA", new Vector3(0.22f, 0.03f, -0.18f), Quaternion.Euler(0f, 24f, 0f), new Vector3(0.10f, 0.035f, 0.42f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cube, "KeyB", new Vector3(-0.18f, 0.03f, -0.22f), Quaternion.Euler(0f, -20f, 0f), new Vector3(0.09f, 0.035f, 0.36f), item.color * 0.9f);
                break;
            case PlaceableShape.SodaCan:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Can", new Vector3(0f, 0.31f, 0f), Quaternion.identity, new Vector3(0.34f, 0.62f, 0.34f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Top", new Vector3(0f, 0.64f, 0f), Quaternion.identity, new Vector3(0.35f, 0.035f, 0.35f), new Color(0.75f, 0.76f, 0.76f));
                AddPrimitive(root.transform, PrimitiveType.Cube, "Label", new Vector3(0f, 0.32f, -0.18f), Quaternion.identity, new Vector3(0.22f, 0.28f, 0.015f), Color.white);
                break;
            case PlaceableShape.PizzaSlice:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Slice", new Vector3(0f, 0.05f, 0f), Quaternion.Euler(0f, 45f, 0f), new Vector3(0.82f, 0.08f, 0.46f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Crust", new Vector3(0.30f, 0.10f, 0.30f), Quaternion.Euler(0f, 45f, 0f), new Vector3(0.52f, 0.08f, 0.10f), new Color(0.62f, 0.34f, 0.13f));
                AddPrimitive(root.transform, PrimitiveType.Sphere, "PepperoniA", new Vector3(-0.06f, 0.14f, 0.02f), Quaternion.identity, new Vector3(0.12f, 0.025f, 0.12f), new Color(0.68f, 0.08f, 0.06f));
                AddPrimitive(root.transform, PrimitiveType.Sphere, "PepperoniB", new Vector3(0.18f, 0.14f, -0.10f), Quaternion.identity, new Vector3(0.12f, 0.025f, 0.12f), new Color(0.68f, 0.08f, 0.06f));
                break;
            case PlaceableShape.Donut:
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Donut", new Vector3(0f, 0.18f, 0f), Quaternion.identity, new Vector3(0.62f, 0.16f, 0.62f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Hole", new Vector3(0f, 0.20f, 0f), Quaternion.identity, new Vector3(0.24f, 0.18f, 0.24f), new Color(0.10f, 0.075f, 0.055f));
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Icing", new Vector3(0f, 0.26f, 0f), Quaternion.identity, new Vector3(0.50f, 0.055f, 0.50f), new Color(0.96f, 0.42f, 0.70f));
                break;
            case PlaceableShape.GameController:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Body", new Vector3(0f, 0.10f, 0f), Quaternion.identity, new Vector3(0.82f, 0.16f, 0.38f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "LeftGrip", new Vector3(-0.42f, 0.03f, 0f), Quaternion.identity, new Vector3(0.28f, 0.24f, 0.32f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "RightGrip", new Vector3(0.42f, 0.03f, 0f), Quaternion.identity, new Vector3(0.28f, 0.24f, 0.32f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Pad", new Vector3(-0.18f, 0.20f, -0.02f), Quaternion.identity, new Vector3(0.18f, 0.025f, 0.18f), new Color(0.18f, 0.19f, 0.21f));
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Button", new Vector3(0.23f, 0.20f, -0.02f), Quaternion.identity, new Vector3(0.08f, 0.025f, 0.08f), new Color(0.10f, 0.55f, 0.85f));
                break;
            case PlaceableShape.Headphones:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "BandTop", new Vector3(0f, 0.56f, 0f), Quaternion.Euler(0f, 0f, 90f), new Vector3(0.045f, 0.64f, 0.045f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "BandLeft", new Vector3(-0.34f, 0.40f, 0f), Quaternion.identity, new Vector3(0.045f, 0.34f, 0.045f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "BandRight", new Vector3(0.34f, 0.40f, 0f), Quaternion.identity, new Vector3(0.045f, 0.34f, 0.045f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cube, "LeftCup", new Vector3(-0.38f, 0.23f, 0f), Quaternion.identity, new Vector3(0.18f, 0.30f, 0.22f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cube, "RightCup", new Vector3(0.38f, 0.23f, 0f), Quaternion.identity, new Vector3(0.18f, 0.30f, 0.22f), item.color);
                break;
            case PlaceableShape.Candle:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Wax", new Vector3(0f, 0.28f, 0f), Quaternion.identity, new Vector3(0.32f, 0.56f, 0.32f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Wick", new Vector3(0f, 0.60f, 0f), Quaternion.identity, new Vector3(0.035f, 0.16f, 0.035f), new Color(0.08f, 0.06f, 0.04f));
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Flame", new Vector3(0f, 0.76f, 0f), Quaternion.identity, new Vector3(0.14f, 0.22f, 0.14f), new Color(1f, 0.62f, 0.08f));
                break;
            case PlaceableShape.PictureFrame:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Back", new Vector3(0f, 0.06f, 0f), Quaternion.identity, new Vector3(0.86f, 0.06f, 0.62f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Picture", new Vector3(0f, 0.105f, 0f), Quaternion.identity, new Vector3(0.68f, 0.018f, 0.44f), new Color(0.36f, 0.56f, 0.70f));
                break;
            case PlaceableShape.Bowl:
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Bowl", new Vector3(0f, 0.22f, 0f), Quaternion.identity, new Vector3(0.62f, 0.30f, 0.62f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Rim", new Vector3(0f, 0.38f, 0f), Quaternion.identity, new Vector3(0.66f, 0.05f, 0.66f), item.color * 0.92f);
                break;
            case PlaceableShape.Spoon:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Handle", new Vector3(0f, 0.05f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.045f, 0.74f, 0.045f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Sphere, "Bowl", new Vector3(0f, 0.05f, -0.42f), Quaternion.identity, new Vector3(0.20f, 0.035f, 0.28f), item.color);
                break;
            case PlaceableShape.Toothbrush:
                AddPrimitive(root.transform, PrimitiveType.Cube, "Handle", new Vector3(0f, 0.045f, 0f), Quaternion.identity, new Vector3(0.12f, 0.08f, 0.86f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Head", new Vector3(0f, 0.08f, -0.48f), Quaternion.identity, new Vector3(0.18f, 0.08f, 0.20f), Color.white);
                AddPrimitive(root.transform, PrimitiveType.Cube, "Bristles", new Vector3(0f, 0.16f, -0.50f), Quaternion.identity, new Vector3(0.15f, 0.12f, 0.13f), new Color(0.75f, 0.90f, 1f));
                break;
            case PlaceableShape.Pen:
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Body", new Vector3(0f, 0.05f, 0f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.07f, 0.86f, 0.07f), item.color);
                AddPrimitive(root.transform, PrimitiveType.Cylinder, "Tip", new Vector3(0f, 0.05f, -0.48f), Quaternion.Euler(90f, 0f, 0f), new Vector3(0.055f, 0.12f, 0.055f), new Color(0.70f, 0.70f, 0.72f));
                break;
        }

        PreparePaletteObject(root);
        return root;
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

    private Vector3 GetFallbackSpawnPoint()
    {
        var camera = Camera.main;
        if (camera != null)
        {
            return camera.transform.position + camera.transform.forward * 2f;
        }
        return Vector3.zero;
    }

    private void PreparePaletteObject(GameObject root)
    {
        var colliders = root.GetComponentsInChildren<Collider>(true);
        foreach (var collider in colliders)
        {
            if (collider != null)
            {
                collider.isTrigger = true;
                collider.enabled = true;
            }
        }

        if (colliders.Length == 0)
        {
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
        }
    }

    private void AddPrimitive(Transform parent, PrimitiveType type, string name, Vector3 localPosition, Quaternion localRotation, Vector3 localScale, Color color)
    {
        var primitive = GameObject.CreatePrimitive(type);
        primitive.name = name;
        primitive.transform.SetParent(parent, false);
        primitive.transform.localPosition = localPosition;
        primitive.transform.localRotation = localRotation;
        primitive.transform.localScale = localScale;

        var renderer = primitive.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = CreateMaterial(color);
        }
    }

    private Material CreateMaterial(Color color)
    {
        var material = new Material(FindSupportedShader(false));
        SetMaterialColor(material, color);
        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0.45f);
        }
        return material;
    }

    private void HandleScroll(Mouse mouse, Keyboard keyboard)
    {
        var scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) < 0.1f)
        {
            return;
        }

        if (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed)
        {
            normalOffset = Mathf.Clamp(normalOffset + Mathf.Sign(scroll) * normalOffsetStep, -0.05f, maxNormalOffset);
        }
        else
        {
            var scaleMultiplier = 1f + Mathf.Sign(scroll) * 0.08f;
            var newScale = heldObject.localScale * scaleMultiplier;
            float uniformScale = Mathf.Clamp(newScale.x, 0.05f, 5.0f);
            heldObject.localScale = Vector3.one * uniformScale;
        }
    }

    private void CacheAndDisablePhysics(Transform item)
    {
        heldColliders = item.GetComponentsInChildren<Collider>(true);
        colliderEnabled = new bool[heldColliders.Length];
        colliderTriggers = new bool[heldColliders.Length];
        for (var i = 0; i < heldColliders.Length; i++)
        {
            var collider = heldColliders[i];
            if (collider == null)
            {
                continue;
            }
            colliderEnabled[i] = collider.enabled;
            colliderTriggers[i] = collider.isTrigger;
            collider.enabled = false;
        }

        heldRigidbodies = item.GetComponentsInChildren<Rigidbody>(true);
        bodyKinematic = new bool[heldRigidbodies.Length];
        bodyGravity = new bool[heldRigidbodies.Length];
        for (var i = 0; i < heldRigidbodies.Length; i++)
        {
            var body = heldRigidbodies[i];
            if (body == null)
            {
                continue;
            }
            bodyKinematic[i] = body.isKinematic;
            bodyGravity[i] = body.useGravity;
            body.isKinematic = true;
            body.useGravity = false;
        }
    }

    private void RestorePhysics()
    {
        for (var i = 0; i < heldColliders.Length; i++)
        {
            var collider = heldColliders[i];
            if (collider == null)
            {
                continue;
            }
            collider.enabled = i < colliderEnabled.Length && colliderEnabled[i];
            collider.isTrigger = i < colliderTriggers.Length && colliderTriggers[i];
        }

        for (var i = 0; i < heldRigidbodies.Length; i++)
        {
            var body = heldRigidbodies[i];
            if (body == null)
            {
                continue;
            }
            body.isKinematic = i < bodyKinematic.Length && bodyKinematic[i];
            body.useGravity = i < bodyGravity.Length && bodyGravity[i];
        }

        heldColliders = Array.Empty<Collider>();
        heldRigidbodies = Array.Empty<Rigidbody>();
        colliderEnabled = Array.Empty<bool>();
        colliderTriggers = Array.Empty<bool>();
        bodyKinematic = Array.Empty<bool>();
        bodyGravity = Array.Empty<bool>();
    }

    private void DrawPalette()
    {
        InitHudStyles();
        InitPaletteStyles();
        if (palette.Count == 0)
        {
            return;
        }

        var panelWidth = Mathf.Min(720f, Screen.width - 32f);
        var panelHeight = 156f;
        var rect = new Rect((Screen.width - panelWidth) * 0.5f, Screen.height - panelHeight - 24f, panelWidth, panelHeight);
        GUI.Box(rect, "Item Wheel", paletteBoxStyle);

        var itemWidth = 104f;
        var spacing = 8f;
        var visibleCount = Mathf.Max(1, Mathf.FloorToInt((panelWidth - 32f) / (itemWidth + spacing)));
        var start = selectedPaletteIndex - visibleCount / 2;
        for (var i = 0; i < visibleCount; i++)
        {
            var index = (start + i + palette.Count) % palette.Count;
            var itemRect = new Rect(rect.x + 16f + i * (itemWidth + spacing), rect.y + 38f, itemWidth, 82f);
            var selected = index == selectedPaletteIndex;
            GUI.Box(itemRect, GUIContent.none, selected ? paletteSelectedStyle : paletteItemStyle);
            DrawPaletteIcon(new Rect(itemRect.x + 20f, itemRect.y + 8f, itemRect.width - 40f, 42f), palette[index]);
            GUI.Label(new Rect(itemRect.x + 4f, itemRect.y + 55f, itemRect.width - 8f, 22f), palette[index].label, selected ? paletteSelectedStyle : paletteItemStyle);
        }

        var hint = "B: close | wheel: choose | RMB: create | LMB: move pointed item | G: pick item";
        GUI.Label(new Rect(rect.x + 16f, rect.y + 120f, rect.width - 32f, 24f), hint, hudStyle);
    }

    private void DrawPlacementCrosshair(bool valid)
    {
        var size = 20f;
        var cx = Screen.width * 0.5f;
        var cy = Screen.height * 0.5f;
        var color = valid ? new Color(0.08f, 0.85f, 1f, 0.96f) : new Color(1f, 0.25f, 0.2f, 0.96f);
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(cx - size * 0.5f - 1f, cy - 2f, size + 2f, 4f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - 2f, cy - size * 0.5f - 1f, 4f, size + 2f), Texture2D.whiteTexture);
        GUI.color = color;
        GUI.DrawTexture(new Rect(cx - size * 0.5f, cy - 1f, size, 2f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - 1f, cy - size * 0.5f, 2f, size), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private void DrawPaletteIcon(Rect rect, PaletteItem item)
    {
        GUI.color = new Color(0.02f, 0.025f, 0.03f, 0.35f);
        GUI.DrawTexture(new Rect(rect.x - 4f, rect.y - 4f, rect.width + 8f, rect.height + 8f), Texture2D.whiteTexture);
        GUI.color = Color.white;

        switch (item.shape)
        {
            case PlaceableShape.Vase:
                FillRect(CenterRect(rect, 26f, 30f, 0f, 6f), item.color);
                FillRect(CenterRect(rect, 14f, 22f, 0f, -12f), item.color * 1.1f);
                FillRect(CenterRect(rect, 34f, 5f, 0f, 20f), item.color * 0.75f);
                break;
            case PlaceableShape.Microphone:
                FillRect(CenterRect(rect, 10f, 30f, 0f, 8f), item.color);
                FillRect(CenterRect(rect, 28f, 20f, 0f, -14f), new Color(0.18f, 0.18f, 0.19f));
                FillRect(CenterRect(rect, 20f, 4f, 0f, 1f), new Color(0.72f, 0.74f, 0.76f));
                break;
            case PlaceableShape.Phone:
                FillRect(CenterRect(rect, 26f, 40f, 0f, 0f), item.color);
                FillRect(CenterRect(rect, 20f, 30f, 0f, -1f), new Color(0.03f, 0.42f, 0.58f));
                break;
            case PlaceableShape.Book:
                FillRect(CenterRect(rect, 42f, 28f, 0f, 4f), item.color);
                FillRect(CenterRect(rect, 32f, 18f, 4f, 4f), new Color(0.92f, 0.86f, 0.68f));
                break;
            case PlaceableShape.Apple:
                FillRect(CenterRect(rect, 30f, 28f, 0f, 5f), item.color);
                FillRect(CenterRect(rect, 5f, 12f, 0f, -15f), new Color(0.34f, 0.18f, 0.08f));
                FillRect(CenterRect(rect, 14f, 5f, 10f, -18f), new Color(0.18f, 0.56f, 0.22f));
                break;
            case PlaceableShape.Cup:
                FillRect(CenterRect(rect, 28f, 30f, -3f, 5f), item.color);
                FillRect(CenterRect(rect, 10f, 18f, 19f, 5f), item.color * 0.85f);
                break;
            case PlaceableShape.Bottle:
                FillRect(CenterRect(rect, 20f, 34f, 0f, 8f), item.color);
                FillRect(CenterRect(rect, 12f, 18f, 0f, -14f), item.color * 0.9f);
                FillRect(CenterRect(rect, 14f, 5f, 0f, -25f), new Color(0.05f, 0.07f, 0.1f));
                break;
            case PlaceableShape.Laptop:
                FillRect(CenterRect(rect, 44f, 10f, 0f, 18f), item.color);
                FillRect(CenterRect(rect, 42f, 28f, 0f, -5f), new Color(0.035f, 0.04f, 0.05f));
                FillRect(CenterRect(rect, 32f, 18f, 0f, -5f), new Color(0.04f, 0.36f, 0.55f));
                break;
            case PlaceableShape.TableLamp:
                FillRect(CenterRect(rect, 36f, 20f, 0f, -8f), item.color);
                FillRect(CenterRect(rect, 6f, 26f, 0f, 10f), new Color(0.45f, 0.38f, 0.28f));
                FillRect(CenterRect(rect, 30f, 5f, 0f, 24f), new Color(0.34f, 0.28f, 0.20f));
                break;
            case PlaceableShape.Plant:
                FillRect(CenterRect(rect, 30f, 18f, 0f, 20f), new Color(0.46f, 0.22f, 0.12f));
                FillRect(CenterRect(rect, 35f, 24f, -5f, -2f), item.color);
                FillRect(CenterRect(rect, 28f, 20f, 12f, -10f), item.color * 1.15f);
                break;
            case PlaceableShape.Remote:
                FillRect(CenterRect(rect, 18f, 42f, 0f, 0f), item.color);
                FillRect(CenterRect(rect, 9f, 9f, 0f, -12f), new Color(0.65f, 0.05f, 0.05f));
                FillRect(CenterRect(rect, 10f, 12f, 0f, 8f), new Color(0.20f, 0.22f, 0.24f));
                break;
            case PlaceableShape.Camera:
                FillRect(CenterRect(rect, 42f, 24f, 0f, 6f), item.color);
                FillRect(CenterRect(rect, 20f, 20f, 0f, 6f), new Color(0.02f, 0.025f, 0.03f));
                FillRect(CenterRect(rect, 16f, 8f, -10f, -12f), item.color * 1.2f);
                break;
            case PlaceableShape.Tablet:
                FillRect(CenterRect(rect, 34f, 46f, 0f, 0f), item.color);
                FillRect(CenterRect(rect, 26f, 36f, 0f, 0f), new Color(0.03f, 0.34f, 0.52f));
                break;
            case PlaceableShape.Notebook:
                FillRect(CenterRect(rect, 38f, 30f, 0f, 4f), item.color);
                FillRect(CenterRect(rect, 28f, 22f, 4f, 4f), new Color(0.93f, 0.91f, 0.82f));
                FillRect(CenterRect(rect, 5f, 32f, -18f, 4f), item.color * 0.75f);
                break;
            case PlaceableShape.Keyring:
                FillRect(CenterRect(rect, 24f, 10f, -8f, -10f), item.color);
                FillRect(CenterRect(rect, 8f, 28f, 8f, 8f), item.color);
                FillRect(CenterRect(rect, 8f, 22f, -8f, 10f), item.color * 0.9f);
                break;
            case PlaceableShape.SodaCan:
                FillRect(CenterRect(rect, 22f, 38f, 0f, 2f), item.color);
                FillRect(CenterRect(rect, 23f, 5f, 0f, -20f), new Color(0.75f, 0.76f, 0.76f));
                FillRect(CenterRect(rect, 15f, 18f, 0f, 2f), Color.white);
                break;
            case PlaceableShape.PizzaSlice:
                FillRect(CenterRect(rect, 42f, 24f, 0f, 6f), item.color);
                FillRect(CenterRect(rect, 28f, 6f, 10f, -8f), new Color(0.62f, 0.34f, 0.13f));
                FillRect(CenterRect(rect, 8f, 8f, -5f, 5f), new Color(0.68f, 0.08f, 0.06f));
                break;
            case PlaceableShape.Donut:
                FillRect(CenterRect(rect, 34f, 26f, 0f, 5f), item.color);
                FillRect(CenterRect(rect, 24f, 14f, 0f, 2f), new Color(0.96f, 0.42f, 0.70f));
                FillRect(CenterRect(rect, 10f, 8f, 0f, 5f), new Color(0.10f, 0.075f, 0.055f));
                break;
            case PlaceableShape.GameController:
                FillRect(CenterRect(rect, 48f, 18f, 0f, 8f), item.color);
                FillRect(CenterRect(rect, 14f, 20f, -22f, 9f), item.color);
                FillRect(CenterRect(rect, 14f, 20f, 22f, 9f), item.color);
                FillRect(CenterRect(rect, 7f, 7f, 12f, 4f), new Color(0.10f, 0.55f, 0.85f));
                break;
            case PlaceableShape.Headphones:
                FillRect(CenterRect(rect, 42f, 6f, 0f, -16f), item.color);
                FillRect(CenterRect(rect, 7f, 28f, -22f, 2f), item.color);
                FillRect(CenterRect(rect, 7f, 28f, 22f, 2f), item.color);
                FillRect(CenterRect(rect, 13f, 20f, -25f, 13f), item.color);
                FillRect(CenterRect(rect, 13f, 20f, 25f, 13f), item.color);
                break;
            case PlaceableShape.Candle:
                FillRect(CenterRect(rect, 22f, 34f, 0f, 8f), item.color);
                FillRect(CenterRect(rect, 4f, 12f, 0f, -13f), new Color(0.08f, 0.06f, 0.04f));
                FillRect(CenterRect(rect, 10f, 14f, 0f, -24f), new Color(1f, 0.62f, 0.08f));
                break;
            case PlaceableShape.PictureFrame:
                FillRect(CenterRect(rect, 46f, 34f, 0f, 3f), item.color);
                FillRect(CenterRect(rect, 34f, 22f, 0f, 3f), new Color(0.36f, 0.56f, 0.70f));
                break;
            case PlaceableShape.Bowl:
                FillRect(CenterRect(rect, 40f, 18f, 0f, 10f), item.color);
                FillRect(CenterRect(rect, 44f, 5f, 0f, -1f), item.color * 0.92f);
                break;
            case PlaceableShape.Spoon:
                FillRect(CenterRect(rect, 6f, 44f, 0f, 5f), item.color);
                FillRect(CenterRect(rect, 16f, 18f, 0f, -21f), item.color);
                break;
            case PlaceableShape.Toothbrush:
                FillRect(CenterRect(rect, 8f, 44f, 0f, 4f), item.color);
                FillRect(CenterRect(rect, 18f, 12f, 0f, -22f), Color.white);
                FillRect(CenterRect(rect, 14f, 8f, 0f, -29f), new Color(0.75f, 0.90f, 1f));
                break;
            case PlaceableShape.Pen:
                FillRect(CenterRect(rect, 7f, 48f, 0f, 3f), item.color);
                FillRect(CenterRect(rect, 7f, 8f, 0f, -25f), new Color(0.70f, 0.70f, 0.72f));
                break;
        }

        GUI.color = Color.white;
    }

    private Rect CenterRect(Rect parent, float width, float height, float offsetX, float offsetY)
    {
        return new Rect(parent.center.x - width * 0.5f + offsetX, parent.center.y - height * 0.5f + offsetY, width, height);
    }

    private void FillRect(Rect rect, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private void InitPaletteStyles()
    {
        if (paletteBoxStyle != null)
        {
            return;
        }

        paletteBoxStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperCenter,
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            padding = new RectOffset(12, 12, 12, 12)
        };
        paletteBoxStyle.normal.textColor = new Color(0.92f, 0.97f, 0.98f);

        paletteItemStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 14,
            fontStyle = FontStyle.Bold,
            wordWrap = true
        };
        paletteItemStyle.normal.textColor = new Color(0.82f, 0.88f, 0.9f);

        paletteSelectedStyle = new GUIStyle(paletteItemStyle);
        paletteSelectedStyle.normal.textColor = Color.white;
    }

    private void ApplyPreviewMaterial(Transform item)
    {
        rendererStates.Clear();
        EnsurePreviewMaterial();
        var renderers = item.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            rendererStates.Add(new RendererState
            {
                renderer = renderer,
                materials = renderer.sharedMaterials
            });

            var previewMaterials = new Material[renderer.sharedMaterials.Length];
            for (var i = 0; i < previewMaterials.Length; i++)
            {
                previewMaterials[i] = previewMaterial;
            }
            renderer.sharedMaterials = previewMaterials;
        }
    }

    private void RestoreMaterials()
    {
        foreach (var state in rendererStates)
        {
            if (state.renderer != null)
            {
                state.renderer.sharedMaterials = state.materials;
            }
        }
        rendererStates.Clear();
    }

    private void EnsurePreviewMaterial()
    {
        if (previewMaterial != null)
        {
            return;
        }

        previewMaterial = new Material(FindSupportedShader(true));
        previewMaterial.name = "Surface Item Preview";
        SetMaterialColor(previewMaterial, new Color(0.08f, 0.78f, 1f, 0.42f));
        if (previewMaterial.HasProperty("_Surface")) previewMaterial.SetFloat("_Surface", 1f);
        if (previewMaterial.HasProperty("_Blend")) previewMaterial.SetFloat("_Blend", 0f);
        if (previewMaterial.HasProperty("_Mode")) previewMaterial.SetFloat("_Mode", 3f);
        previewMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        previewMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        previewMaterial.SetInt("_ZWrite", 0);
        previewMaterial.DisableKeyword("_ALPHATEST_ON");
        previewMaterial.EnableKeyword("_ALPHABLEND_ON");
        previewMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        previewMaterial.renderQueue = 3000;
    }

    private Shader FindSupportedShader(bool transparent)
    {
        var shader = Shader.Find(transparent ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit");
        if (shader != null) return shader;
        shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader != null) return shader;
        shader = Shader.Find("Sprites/Default");
        if (shader != null) return shader;
        shader = Shader.Find("Unlit/Color");
        if (shader != null) return shader;
        return Shader.Find("Standard");
    }

    private void SetMaterialColor(Material material, Color color)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
        material.color = color;
    }

    private bool TryGetBestHit(Ray ray, float distance, Transform ignoredRoot, out RaycastHit bestHit)
    {
        bestHit = default;
        var hits = Physics.RaycastAll(ray, distance, ~0, QueryTriggerInteraction.Collide);
        var bestDistance = float.MaxValue;
        var found = false;

        for (var i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];
            if (hit.transform == null)
            {
                continue;
            }
            if (ignoredRoot != null && hit.transform.IsChildOf(ignoredRoot))
            {
                continue;
            }
            if (hit.transform.GetComponentInParent<PlayerMove>() != null)
            {
                continue;
            }
            if (hit.transform.GetComponentInParent<SurfaceItemPlacer>() != null)
            {
                continue;
            }
            if (hit.distance < bestDistance)
            {
                bestDistance = hit.distance;
                bestHit = hit;
                found = true;
            }
        }

        return found;
    }

    private Transform FindPlaceableRoot(Transform hit)
    {
        if (hit == null || hit.GetComponentInParent<PlayerMove>() != null || hit.GetComponentInParent<StudioBuildBlock>() != null)
        {
            return null;
        }

        var usable = hit.GetComponentInParent<SimpleUsableProp>();
        if (usable != null)
        {
            return usable.transform;
        }

        var current = hit;
        while (current.parent != null)
        {
            var parentName = current.parent.name;
            if (parentName == "SpawnedInventoryProps" || parentName == "DownloadedFurnitureProps")
            {
                return current;
            }
            current = current.parent;
        }

        return null;
    }

    private Bounds GetRendererBounds(Transform root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(root.position, Vector3.one * 0.25f);
        }

        var bounds = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        return bounds;
    }

    private float GetProjectedExtent(Vector3 extents, Vector3 normal)
    {
        normal = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
        return extents.x * normal.x + extents.y * normal.y + extents.z * normal.z;
    }

    private void InitHudStyles()
    {
        if (hudStyle != null)
        {
            return;
        }

        hudStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            wordWrap = true
        };
        hudStyle.normal.textColor = new Color(0.90f, 0.98f, 1f);

        hudShadowStyle = new GUIStyle(hudStyle);
        hudShadowStyle.normal.textColor = Color.black;
    }
}
