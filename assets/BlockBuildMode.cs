using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class BlockBuildMode : MonoBehaviour
{
    public enum BlockKind
    {
        Solid,
        Water
    }

    public static BlockBuildMode Instance { get; private set; }
    public static bool IsBuildMode { get; set; }

    public Key toggleKey = Key.F2;
    public Key undoKey = Key.Z;
    public float blockSize = 1f;
    public float overlap = 0.003f;
    public BlockKind selectedKind = BlockKind.Solid;
    public int selectedColorIndex;
    public Color blockColor = new Color(0.72f, 0.74f, 0.76f, 1f);
    public Color waterColor = new Color(0.18f, 0.68f, 0.95f, 0.48f);
    public Color[] palette =
    {
        new Color(0.72f, 0.74f, 0.76f, 1f),
        new Color(0.95f, 0.95f, 0.9f, 1f),
        new Color(0.18f, 0.18f, 0.2f, 1f),
        new Color(0.72f, 0.12f, 0.12f, 1f),
        new Color(0.1f, 0.42f, 0.82f, 1f),
        new Color(0.08f, 0.58f, 0.24f, 1f),
        new Color(0.92f, 0.76f, 0.18f, 1f),
        new Color(0.52f, 0.25f, 0.8f, 1f),
        new Color(0.95f, 0.45f, 0.1f, 1f),
        new Color(0.05f, 0.78f, 0.76f, 1f),
    };

    private readonly Dictionary<int, Material> blockMaterials = new();
    private Material waterMaterial;
    private readonly Stack<GameObject> placedBlocks = new();
    private Transform blockParent;
    private GameObject previewBlock;
    private Material previewMaterial;
    private Texture2D crosshairTexture;
    private bool isRemoveMode;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (FindAnyObjectByType<BlockBuildMode>() != null)
        {
            return;
        }

        var go = new GameObject("BlockBuildMode");
        DontDestroyOnLoad(go);
        go.AddComponent<BlockBuildMode>();
    }

    private void Awake()
    {
        Instance = this;
        enabled = true;
        IsBuildMode = false;
        isRemoveMode = false;
        selectedColorIndex = Mathf.Clamp(selectedColorIndex, 0, Mathf.Max(0, palette.Length - 1));
        blockColor = GetSelectedColor();
        EnsureBlockParent();
        EnsurePreview();
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        var mouse = Mouse.current;

        if (SurfaceItemPlacer.BlocksToolInput || CharacterTuningMenu.IsOpen)
        {
            return;
        }

        if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
        {
            IsBuildMode = !IsBuildMode;
            Debug.Log($"Build Mode Toggled: {IsBuildMode}");
            SetCursorForBuildMode();
        }

        if (!IsBuildMode || SurfaceItemPlacer.BlocksToolInput || SimpleInventoryWindow.IsOpen || MapSwitcher.IsMenuOpen || CharacterActionRecorder.IsMenuOpen || CharacterTuningMenu.IsOpen)
        {
            return;
        }

        if (mouse != null)
        {
            var scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.1f)
            {
                isRemoveMode = scroll < 0;
            }
        }

        if (keyboard != null && keyboard[undoKey].wasPressedThisFrame)
        {
            UndoLastBlock();
        }

        UpdatePreview();
        SetCursorForBuildMode();

        var camera = Camera.main;
        if (mouse == null || camera == null) return;

        var screenPosition = GetBuildScreenPosition(mouse);
        
        if (mouse.rightButton.wasPressedThisFrame)
        {
            Debug.Log("RMB Pressed - Attempting to Place Block");
            PlaceBlock(camera, screenPosition);
        }
        else if (mouse.leftButton.wasPressedThisFrame)
        {
            Debug.Log("LMB Pressed - Attempting to Remove Block");
            RemoveBlock(camera, screenPosition);
        }
    }

    private void OnGUI()
    {
        var mouse = Mouse.current;
        var showCrosshair = (IsBuildMode && !SurfaceItemPlacer.BlocksToolInput && !CharacterTuningMenu.IsOpen)
            || (mouse != null && mouse.rightButton.isPressed && !SimpleInventoryWindow.IsOpen && !CharacterActionRecorder.IsMenuOpen && !CharacterTuningMenu.IsOpen && !SurfaceItemPlacer.BlocksToolInput);
        if (!showCrosshair)
        {
            return;
        }

        DrawCrosshair();

        if (IsBuildMode)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                alignment = TextAnchor.UpperCenter,
                fontStyle = FontStyle.Bold
            };
            
            // Add a background shadow for better readability
            var shadowStyle = new GUIStyle(style);
            shadowStyle.normal.textColor = Color.black;
            var modeLabel = isRemoveMode ? "< REMOVE MODE >" : selectedKind == BlockKind.Water ? "[ WATER MODE ]" : "[ BUILD MODE ]";
            GUI.Label(new Rect(2, 22, Screen.width, 40), modeLabel, shadowStyle);

            GUI.color = isRemoveMode ? Color.red : selectedKind == BlockKind.Water ? new Color(0.2f, 0.85f, 1f, 1f) : Color.cyan;
            GUI.Label(new Rect(0, 20, Screen.width, 40), modeLabel, style);
            GUI.color = Color.white;
            
            GUI.Label(new Rect(20, Screen.height - 40, 400, 40), "RMB: Place | LMB: Remove | Scroll: Change Mode", style);
        }
    }

    public Color GetSelectedColor()
    {
        if (palette == null || palette.Length == 0)
        {
            return blockColor;
        }

        return palette[Mathf.Clamp(selectedColorIndex, 0, palette.Length - 1)];
    }

    public void SelectColor(int index)
    {
        if (palette == null || palette.Length == 0)
        {
            return;
        }

        selectedColorIndex = Mathf.Clamp(index, 0, palette.Length - 1);
        blockColor = palette[selectedColorIndex];
        UpdatePreviewMaterial();
    }

    public void SelectKind(BlockKind kind)
    {
        selectedKind = kind;
        UpdatePreviewMaterial();
    }

    public void ClearBlocks()
    {
        EnsureBlockParent();
        for (var i = blockParent.childCount - 1; i >= 0; i--)
        {
            Destroy(blockParent.GetChild(i).gameObject);
        }

        var looseWater = FindObjectsByType<StudioWaterVolume>(FindObjectsInactive.Include);
        for (var i = 0; i < looseWater.Length; i++)
        {
            if (looseWater[i] != null)
            {
                Destroy(looseWater[i].gameObject);
            }
        }

        placedBlocks.Clear();
    }

    public void UndoLastBlock()
    {
        while (placedBlocks.Count > 0)
        {
            var block = placedBlocks.Pop();
            if (block != null)
            {
                Destroy(block);
                return;
            }
        }

        EnsureBlockParent();
        if (blockParent.childCount > 0)
        {
            Destroy(blockParent.GetChild(blockParent.childCount - 1).gameObject);
        }
    }

    private void PlaceBlock(Camera camera, Vector2 screenPosition)
    {
        EnsureBlockParent();
        if (blockParent != null) blockParent.gameObject.SetActive(true);

        var ray = camera.ScreenPointToRay(screenPosition);
        var target = GetTargetPosition(ray);
        if (HasBlockAt(target))
        {
            return;
        }

        var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = selectedKind == BlockKind.Water ? "StudioWaterBlock" : "StudioBlock";
        block.transform.SetParent(blockParent, true);
        block.transform.position = target;
        block.transform.localScale = Vector3.one * Mathf.Max(0.01f, blockSize + overlap);
        block.layer = 0; // Default layer
        
        if (block.GetComponent<StudioBuildBlock>() == null)
            block.AddComponent<StudioBuildBlock>();
        if (selectedKind == BlockKind.Water && block.GetComponent<StudioWaterVolume>() == null)
            block.AddComponent<StudioWaterVolume>();

        var renderer = block.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = selectedKind == BlockKind.Water ? GetWaterMaterial() : GetBlockMaterial(GetSelectedColor());
            renderer.enabled = true;
        }

        var collider = block.GetComponent<Collider>();
        if (collider != null)
        {
            collider.isTrigger = selectedKind == BlockKind.Water;
            collider.enabled = true;
        }

        placedBlocks.Push(block);
    }

    public GameObject CreateWaterVolume(string objectName, Vector3 position, Vector3 scale, Transform parent = null)
    {
        EnsureBlockParent();
        var water = GameObject.CreatePrimitive(PrimitiveType.Cube);
        water.name = string.IsNullOrEmpty(objectName) ? "StudioWaterVolume" : objectName;
        water.transform.SetParent(parent != null ? parent : blockParent, true);
        water.transform.position = position;
        water.transform.localScale = new Vector3(
            Mathf.Max(0.05f, scale.x),
            Mathf.Max(0.02f, scale.y),
            Mathf.Max(0.05f, scale.z));

        if (water.GetComponent<StudioBuildBlock>() == null)
            water.AddComponent<StudioBuildBlock>();
        if (water.GetComponent<StudioWaterVolume>() == null)
            water.AddComponent<StudioWaterVolume>();

        var renderer = water.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = GetWaterMaterial();
        }

        var collider = water.GetComponent<Collider>();
        if (collider != null)
        {
            collider.isTrigger = true;
            collider.enabled = true;
        }

        placedBlocks.Push(water);
        return water;
    }

    private bool TryGetValidHit(Ray ray, out RaycastHit validHit)
    {
        validHit = default;
        var hits = Physics.RaycastAll(ray, 1000f, ~0, QueryTriggerInteraction.Collide);
        var nearestDistance = float.MaxValue;
        var found = false;

        for (int i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];
            if (hit.transform == null) continue;
            
            if (previewBlock != null && hit.transform.gameObject == previewBlock) continue;
            if (hit.transform.CompareTag("Player")) continue;
            if (hit.transform.GetComponentInParent<PlayerMove>() != null) continue;

            if (hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
                validHit = hit;
                found = true;
            }
        }
        
        return found;
    }

    private void RemoveBlock(Camera camera, Vector2 screenPosition)
    {
        var ray = camera.ScreenPointToRay(screenPosition);
        if (!TryGetValidHit(ray, out var hit))
        {
            return;
        }

        GameObject targetObject = null;
        var marker = hit.transform.GetComponentInParent<StudioBuildBlock>();
        if (marker != null)
        {
            targetObject = marker.gameObject;
        }
        else if (hit.transform.name.Contains("StudioBlock"))
        {
            targetObject = hit.transform.gameObject;
        }

        if (targetObject != null && targetObject != previewBlock)
        {
            Destroy(targetObject);
        }
    }

    private Vector3 GetTargetPosition(Ray ray)
    {
        if (TryGetValidHit(ray, out var hit))
        {
            var marker = hit.transform.GetComponentInParent<StudioBuildBlock>();
            if (marker != null)
            {
                var normal = SnapNormal(hit.normal);
                Vector3 pos = marker.transform.position + normal * blockSize;
                return pos;
            }
            else
            {
                var normal = SnapNormal(hit.normal);
                Vector3 pos = SnapToGrid(hit.point + normal * (blockSize * 0.5f));
                return pos;
            }
        }

        var plane = new Plane(Vector3.up, Vector3.zero);
        if (plane.Raycast(ray, out var enter))
        {
            var point = ray.GetPoint(enter);
            point.y = blockSize * 0.5f;
            return SnapToGrid(point);
        }

        return SnapToGrid(ray.origin + ray.direction * 4f);
    }

    private void UpdatePreview()
    {
        EnsurePreview();
        if (previewBlock == null)
        {
            return;
        }

        // Permanently disable the preview block as requested by the user
        previewBlock.SetActive(false);
    }

    private Vector2 GetBuildScreenPosition(Mouse mouse)
    {
        if (Cursor.lockState == CursorLockMode.Locked || mouse.rightButton.isPressed)
        {
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }

        return mouse.position.ReadValue();
    }

    private Vector3 SnapToGrid(Vector3 point)
    {
        var size = Mathf.Max(0.01f, blockSize);
        return new Vector3(
            Mathf.Round(point.x / size) * size,
            Mathf.Round(point.y / size) * size,
            Mathf.Round(point.z / size) * size);
    }

    private Vector3 SnapNormal(Vector3 normal)
    {
        var abs = new Vector3(Mathf.Abs(normal.x), Mathf.Abs(normal.y), Mathf.Abs(normal.z));
        if (abs.x >= abs.y && abs.x >= abs.z)
        {
            return new Vector3(Mathf.Sign(normal.x), 0f, 0f);
        }
        if (abs.y >= abs.x && abs.y >= abs.z)
        {
            return new Vector3(0f, Mathf.Sign(normal.y), 0f);
        }
        return new Vector3(0f, 0f, Mathf.Sign(normal.z));
    }

    private bool HasBlockAt(Vector3 position)
    {
        EnsureBlockParent();
        for (var i = 0; i < blockParent.childCount; i++)
        {
            if ((blockParent.GetChild(i).position - position).sqrMagnitude < 0.0001f)
            {
                return true;
            }
        }
        return false;
    }

    private void EnsureBlockParent()
    {
        if (blockParent != null)
        {
            return;
        }

        var existing = GameObject.Find("StudioBlocks");
        if (existing == null)
        {
            existing = new GameObject("StudioBlocks");
        }
        blockParent = existing.transform;
    }

    private void EnsurePreview()
    {
        if (previewBlock != null)
        {
            return;
        }

        previewBlock = GameObject.CreatePrimitive(PrimitiveType.Cube);
        previewBlock.name = "StudioBlockPreview";
        previewBlock.hideFlags = HideFlags.DontSave;
        previewBlock.AddComponent<StudioBuildBlock>();
        var collider = previewBlock.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }
        previewBlock.SetActive(false);
        UpdatePreviewMaterial();
    }

    private void UpdatePreviewMaterial()
    {
        if (previewBlock == null)
        {
            return;
        }

        if (previewMaterial == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            previewMaterial = new Material(shader);
            previewMaterial.name = "StudioBlockPreview_Mat";
            SetSurfaceTransparent(previewMaterial);
        }

        var color = selectedKind == BlockKind.Water ? waterColor : GetSelectedColor();
        color.a = 0.42f;
        SetMaterialColor(previewMaterial, color);

        var renderer = previewBlock.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = previewMaterial;
        }
    }

    private Material GetBlockMaterial(Color color)
    {
        var key = ColorKey(color);
        if (blockMaterials.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader);
        material.name = "StudioBlock_" + key;
        SetMaterialColor(material, color);
        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0.18f);
        }
        blockMaterials[key] = material;
        return material;
    }

    private Material GetWaterMaterial()
    {
        if (waterMaterial != null)
        {
            return waterMaterial;
        }

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        waterMaterial = new Material(shader);
        waterMaterial.name = "StudioWater_Mat";
        SetSurfaceTransparent(waterMaterial);
        SetMaterialColor(waterMaterial, waterColor);
        if (waterMaterial.HasProperty("_Smoothness"))
        {
            waterMaterial.SetFloat("_Smoothness", 0.86f);
        }
        if (waterMaterial.HasProperty("_Metallic"))
        {
            waterMaterial.SetFloat("_Metallic", 0f);
        }
        return waterMaterial;
    }

    private int ColorKey(Color color)
    {
        var r = Mathf.RoundToInt(color.r * 255f);
        var g = Mathf.RoundToInt(color.g * 255f);
        var b = Mathf.RoundToInt(color.b * 255f);
        return (r << 16) | (g << 8) | b;
    }

    private void SetMaterialColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }
    }

    private void SetSurfaceTransparent(Material material)
    {
        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        material.renderQueue = 3000;
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHAPREMULTIPLY_ON");
    }

    private void SetCursorForBuildMode()
    {
        if (IsBuildMode)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else if (!SimpleInventoryWindow.IsOpen && !CharacterActionRecorder.IsMenuOpen && !CharacterTuningMenu.IsOpen && !SurfaceItemPlacer.BlocksToolInput)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void DrawCrosshair()
    {
        if (crosshairTexture == null)
        {
            crosshairTexture = Texture2D.whiteTexture;
        }

        var x = Screen.width * 0.5f;
        var y = Screen.height * 0.5f;
        DrawCrosshairLine(new Rect(x - 9f, y - 1f, 18f, 2f), Color.black);
        DrawCrosshairLine(new Rect(x - 1f, y - 9f, 2f, 18f), Color.black);
        DrawCrosshairLine(new Rect(x - 8f, y, 16f, 1f), Color.white);
        DrawCrosshairLine(new Rect(x, y - 8f, 1f, 16f), Color.white);
    }

    private void DrawCrosshairLine(Rect rect, Color color)
    {
        var oldColor = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, crosshairTexture);
        GUI.color = oldColor;
    }
}

public class StudioBuildBlock : MonoBehaviour
{
}

public class StudioWaterVolume : MonoBehaviour
{
    private Vector3 baseScale;
    private Vector3 basePosition;
    private float phase;

    private void Awake()
    {
        baseScale = transform.localScale;
        basePosition = transform.localPosition;
        phase = UnityEngine.Random.value * 10f;
    }

    private void Update()
    {
        var wave = Mathf.Sin(Time.time * 1.7f + phase) * 0.006f;
        transform.localScale = new Vector3(baseScale.x, Mathf.Max(0.01f, baseScale.y + wave), baseScale.z);
        transform.localPosition = basePosition + Vector3.up * (wave * 0.35f);
    }
}
