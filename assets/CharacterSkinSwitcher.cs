using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class CharacterSkinSwitcher : MonoBehaviour
{
    public static CharacterSkinSwitcher Instance { get; private set; }

    [System.Serializable]
    public class SkinEntry
    {
        public string label;
        public string templateName;
    }

    public Key cycleSkinKey = Key.K;
    public SkinEntry[] skins =
    {
        new SkinEntry { label = "Rumi", templateName = "PlayerModel" },
        new SkinEntry { label = "Zoey", templateName = "ZoeyModel" },
        new SkinEntry { label = "Mario", templateName = "MarioModel" },
        new SkinEntry { label = "Luigi", templateName = "LuigiModel" }
    };

    private readonly Dictionary<PlayerMove, CharacterSkinState> states = new();
    private readonly Dictionary<string, Transform> templateCache = new();

    private class CharacterSkinState
    {
        public int index;
        public GameObject clone;
        public readonly List<GameObject> originalVisualRoots = new();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (FindAnyObjectByType<CharacterSkinSwitcher>() != null)
        {
            return;
        }

        var go = new GameObject("CharacterSkinSwitcher");
        DontDestroyOnLoad(go);
        go.AddComponent<CharacterSkinSwitcher>();
    }

    private void Awake()
    {
        Instance = this;
        EnsureDefaultSkins();
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null
            || SimpleInventoryWindow.IsOpen
            || MapSwitcher.IsMenuOpen
            || CharacterActionRecorder.IsMenuOpen
            || CharacterTuningMenu.IsOpen
            || SurfaceItemPlacer.BlocksToolInput)
        {
            return;
        }

        if (keyboard[cycleSkinKey].wasPressedThisFrame)
        {
            CycleActiveCharacterSkin();
        }
    }

    public void CycleActiveCharacterSkin()
    {
        var character = FindActiveCharacter();
        if (character == null)
        {
            return;
        }

        var available = GetAvailableSkins();
        if (available.Count == 0)
        {
            Debug.LogWarning("[SkinSwitcher] No skin templates found. Expected PlayerModel, ZoeyModel, MarioModel, or LuigiModel.");
            return;
        }

        var state = GetState(character);
        state.index = (state.index + 1) % available.Count;
        ApplySkin(character, available[state.index], state);
    }

    public void ApplySkinByName(PlayerMove character, string label)
    {
        if (character == null || string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        var available = GetAvailableSkins();
        var index = available.FindIndex(item => string.Equals(item.label, label, System.StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return;
        }

        var state = GetState(character);
        state.index = index;
        ApplySkin(character, available[index], state);
    }

    private void ApplySkin(PlayerMove character, SkinEntry skin, CharacterSkinState state)
    {
        var root = character.transform;
        var template = FindTemplate(skin.templateName);
        if (template == null || template.GetComponentInChildren<Renderer>(true) == null)
        {
            Debug.LogWarning("[SkinSwitcher] Missing visible template for skin: " + skin.label);
            return;
        }

        CacheOriginalVisualRoots(root, state);
        var templateBelongsToCharacter = template.GetComponentInParent<PlayerMove>() == character;
        SetOriginalVisualsActive(state, templateBelongsToCharacter);

        if (state.clone != null)
        {
            Destroy(state.clone);
            state.clone = null;
        }

        if (templateBelongsToCharacter)
        {
            Debug.Log("[SkinSwitcher] " + CleanName(character.name) + " skin: " + skin.label);
            return;
        }

        var clone = Instantiate(template.gameObject);
        clone.name = "ActiveSkin_" + skin.label;
        clone.transform.SetParent(root, false);
        clone.transform.localPosition = template.localPosition;
        clone.transform.localRotation = template.localRotation;
        clone.transform.localScale = template.localScale;
        clone.SetActive(true);
        EnableCloneRenderers(clone);

        PrepareCloneForCharacter(clone, character);
        state.clone = clone;
        Debug.Log("[SkinSwitcher] " + CleanName(character.name) + " skin: " + skin.label);
    }

    private CharacterSkinState GetState(PlayerMove character)
    {
        if (!states.TryGetValue(character, out var state))
        {
            state = new CharacterSkinState();
            states[character] = state;
            CacheOriginalVisualRoots(character.transform, state);
            state.index = FindOriginalSkinIndex(character);
        }

        return state;
    }

    private void EnsureDefaultSkins()
    {
        var needsDefaults = skins == null
            || skins.Length < 4
            || !skins.Any(item => item != null && string.Equals(item.label, "Mario", System.StringComparison.OrdinalIgnoreCase) && string.Equals(item.templateName, "MarioModel", System.StringComparison.OrdinalIgnoreCase))
            || !skins.Any(item => item != null && string.Equals(item.label, "Luigi", System.StringComparison.OrdinalIgnoreCase) && string.Equals(item.templateName, "LuigiModel", System.StringComparison.OrdinalIgnoreCase));

        if (!needsDefaults)
        {
            return;
        }

        skins = new[]
        {
            new SkinEntry { label = "Rumi", templateName = "PlayerModel" },
            new SkinEntry { label = "Zoey", templateName = "ZoeyModel" },
            new SkinEntry { label = "Mario", templateName = "MarioModel" },
            new SkinEntry { label = "Luigi", templateName = "LuigiModel" }
        };
    }

    private int FindOriginalSkinIndex(PlayerMove character)
    {
        var available = GetAvailableSkins();
        for (var i = 0; i < available.Count; i++)
        {
            var template = FindTemplate(available[i].templateName);
            if (template != null && template.GetComponentInParent<PlayerMove>() == character)
            {
                return i;
            }
        }

        return -1;
    }

    private List<SkinEntry> GetAvailableSkins()
    {
        return skins
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.label) && FindTemplate(item.templateName) != null)
            .ToList();
    }

    private Transform FindTemplate(string templateName)
    {
        if (string.IsNullOrWhiteSpace(templateName))
        {
            return null;
        }

        templateName = NormalizeTemplateName(templateName);

        if (templateCache.TryGetValue(templateName, out var cached) && cached != null)
        {
            return cached;
        }

        var all = FindObjectsByType<Transform>(FindObjectsInactive.Include);
        foreach (var item in all)
        {
            if (item != null && item.name == templateName)
            {
                templateCache[templateName] = item;
                return item;
            }
        }

        return null;
    }

    private static string NormalizeTemplateName(string templateName)
    {
        if (templateName.IndexOf("Mario", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "MarioModel";
        }

        if (templateName.IndexOf("Luigi", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "LuigiModel";
        }

        return templateName;
    }

    private static PlayerMove FindActiveCharacter()
    {
        var switcher = FindAnyObjectByType<CharacterSwitcher>();
        if (switcher != null && switcher.ActiveCharacter != null)
        {
            return switcher.ActiveCharacter;
        }

        var characters = FindObjectsByType<PlayerMove>(FindObjectsInactive.Include);
        foreach (var character in characters)
        {
            if (character != null && character.enabled && character.gameObject.activeInHierarchy)
            {
                return character;
            }
        }

        return null;
    }

    private static void CacheOriginalVisualRoots(Transform root, CharacterSkinState state)
    {
        if (root == null || state.originalVisualRoots.Count > 0)
        {
            return;
        }

        for (var i = 0; i < root.childCount; i++)
        {
            var child = root.GetChild(i);
            if (child == null || child.name.StartsWith("ActiveSkin_", System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (child.GetComponentInChildren<Renderer>(true) != null
                && (child.GetComponentInChildren<Animator>(true) != null
                    || child.GetComponentInChildren<RumiStarterAssetsAnimatorDriver>(true) != null
                    || child.name.EndsWith("Model", System.StringComparison.OrdinalIgnoreCase)))
            {
                state.originalVisualRoots.Add(child.gameObject);
            }
        }
    }

    private static void SetOriginalVisualsActive(CharacterSkinState state, bool active)
    {
        foreach (var visual in state.originalVisualRoots)
        {
            if (visual != null)
            {
                visual.SetActive(active);
            }
        }
    }

    private static void PrepareCloneForCharacter(GameObject clone, PlayerMove character)
    {
        foreach (var collider in clone.GetComponentsInChildren<Collider>(true))
        {
            collider.enabled = false;
        }

        foreach (var body in clone.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.useGravity = false;
        }

        var animator = clone.GetComponentInChildren<Animator>(true);
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }

        var driver = clone.GetComponentInChildren<RumiStarterAssetsAnimatorDriver>(true);
        if (driver == null && animator != null && animator.avatar != null && animator.avatar.isHuman)
        {
            driver = animator.gameObject.AddComponent<RumiStarterAssetsAnimatorDriver>();
        }

        if (driver != null)
        {
            driver.controller = character;
            driver.animator = animator;
            driver.AutoWire();
        }

        var interaction = clone.GetComponentInChildren<RumiInteractionAnimationController>(true);
        if (interaction == null && animator != null && animator.avatar != null && animator.avatar.isHuman)
        {
            interaction = animator.gameObject.AddComponent<RumiInteractionAnimationController>();
        }

        if (interaction != null)
        {
            interaction.animator = animator;
        }

        var hotkeys = clone.GetComponentInChildren<RumiDownloadedAnimationHotkeys>(true);
        if (hotkeys == null && animator != null && animator.avatar != null && animator.avatar.isHuman)
        {
            hotkeys = animator.gameObject.AddComponent<RumiDownloadedAnimationHotkeys>();
        }

        if (hotkeys != null)
        {
            hotkeys.animator = animator;
            hotkeys.controller = character;
        }

        var staticSkinAnimator = clone.GetComponentInChildren<MarioStaticSkinAnimator>(true);
        var isStaticMascotSkin = clone.name.IndexOf("Mario", System.StringComparison.OrdinalIgnoreCase) >= 0
            || clone.name.IndexOf("Luigi", System.StringComparison.OrdinalIgnoreCase) >= 0;

        if (staticSkinAnimator == null && isStaticMascotSkin)
        {
            staticSkinAnimator = clone.AddComponent<MarioStaticSkinAnimator>();
        }

        if (staticSkinAnimator != null)
        {
            staticSkinAnimator.Bind(character);
        }
    }

    private static void EnableCloneRenderers(GameObject clone)
    {
        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = true;
        }
    }

    private static string CleanName(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Character" : value.Replace("(Clone)", string.Empty).Trim();
    }
}
