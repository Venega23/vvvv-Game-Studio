using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class RumiDownloadedAnimationHotkeys : MonoBehaviour
{
#if UNITY_EDITOR
    private const string CatalogPath = "Assets/DownloadedAnimations/hotkeys.json";
#endif

    [System.Serializable]
    public class HotkeyAnimation
    {
        public string label;
        public Key key;
        public AnimationClip clip;
        public bool loop;
    }

    [Header("Keys")]
    public Key stopKey = Key.Digit0;
    public HotkeyAnimation[] animations =
    {
        new HotkeyAnimation { label = "Pointing", key = Key.Digit1, loop = true },
        new HotkeyAnimation { label = "Silly Dancing", key = Key.Digit5, loop = true },
        new HotkeyAnimation { label = "Hip Hop Dancing", key = Key.Digit6, loop = true },
        new HotkeyAnimation { label = "Breakdance Uprock", key = Key.Digit7, loop = true },
        new HotkeyAnimation { label = "Breakdance Freeze", key = Key.Digit8, loop = false },
        new HotkeyAnimation { label = "Fight Idle", key = Key.Digit9, loop = true },
        new HotkeyAnimation { label = "Talking On Phone", key = Key.P, loop = true }
    };

    [Header("Runtime")]
    public Animator animator;
    public PlayerMove controller;

    private PlayableGraph graph;
    private AnimationClipPlayable clipPlayable;
    private HotkeyAnimation active;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        var model = GameObject.Find("PlayerModel");
        if (model != null && model.GetComponent<RumiDownloadedAnimationHotkeys>() == null)
        {
            model.AddComponent<RumiDownloadedAnimationHotkeys>();
        }
    }

    private void Awake()
    {
        AutoWire();
        AssignDownloadedClipsInEditor();
    }

    private void Start()
    {
        AutoWire();
        StopAnimation();
        if (animator != null)
        {
            animator.Rebind();
            animator.Update(0f);
        }
    }

    private void OnDisable()
    {
        StopAnimation(false);
    }

    private void OnDestroy()
    {
        StopAnimation(false);
    }

    private void Update()
    {
        AutoWire();

        if (controller != null && !controller.enabled)
        {
            return;
        }

        var keyboard = Keyboard.current;
        if (keyboard == null || SimpleInventoryWindow.IsOpen || CharacterActionRecorder.IsMenuOpen || CharacterTuningMenu.IsOpen || SurfaceItemPlacer.BlocksToolInput)
        {
            return;
        }

        if (active != null && controller != null && keyboard[controller.toggleStrutKey].wasPressedThisFrame)
        {
            StopAnimation(false);
            return;
        }

        if (active != null && ShouldReturnToLocomotion(keyboard))
        {
            StopAnimation();
            return;
        }

        if (keyboard[stopKey].wasPressedThisFrame)
        {
            StopAnimation();
            return;
        }

        for (var i = 0; i < animations.Length; i++)
        {
            var item = animations[i];
            if (item == null || item.clip == null)
            {
                continue;
            }

            if (keyboard[item.key].wasPressedThisFrame)
            {
                if (active == item)
                {
                    StopAnimation();
                }
                else
                {
                    Play(item);
                }
                return;
            }
        }

        if (active != null && !active.loop && clipPlayable.IsValid() && clipPlayable.GetTime() >= active.clip.length - 0.05f)
        {
            StopAnimation();
        }
    }

    private bool ShouldReturnToLocomotion(Keyboard keyboard)
    {
        return keyboard.wKey.isPressed
            || keyboard.aKey.isPressed
            || keyboard.sKey.isPressed
            || keyboard.dKey.isPressed
            || keyboard.spaceKey.wasPressedThisFrame
            || keyboard.leftCtrlKey.isPressed
            || keyboard.rightCtrlKey.isPressed
            || keyboard.leftShiftKey.isPressed
            || keyboard.rightShiftKey.isPressed;
    }

    private void Play(HotkeyAnimation item)
    {
        if (item == null || item.clip == null)
        {
            return;
        }

        AutoWire();
        if (animator == null)
        {
            return;
        }

        StopAnimation();
        active = item;

        graph = PlayableGraph.Create("Rumi Downloaded Animation Hotkey");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        clipPlayable = AnimationClipPlayable.Create(graph, item.clip);
        clipPlayable.SetApplyFootIK(true);
        clipPlayable.SetApplyPlayableIK(false);
        clipPlayable.SetDuration(item.clip.length);
        clipPlayable.SetTime(0d);

        var output = AnimationPlayableOutput.Create(graph, "DownloadedHotkeyAnimation", animator);
        output.SetSourcePlayable(clipPlayable);
        graph.Play();
    }

    public void StopAnimation(bool clearControllerInput = true, bool resetAnimatorPose = false)
    {
        if (graph.IsValid())
        {
            graph.Destroy();
        }

        active = null;
        if (clearControllerInput && controller != null)
        {
            controller.ClearInputState();
        }

        if (resetAnimatorPose && animator != null)
        {
            animator.Rebind();
            animator.Update(0f);
        }
    }

    public static void StopAll(bool resetAnimatorPose)
    {
        var all = FindObjectsByType<RumiDownloadedAnimationHotkeys>(FindObjectsInactive.Include);
        for (var i = 0; i < all.Length; i++)
        {
            if (all[i] != null)
            {
                all[i].StopAnimation(false, resetAnimatorPose);
            }
        }
    }

    private void AutoWire()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
        }

        if (controller == null)
        {
            controller = GetComponentInParent<PlayerMove>();
        }
    }

    private void AssignDownloadedClipsInEditor()
    {
#if UNITY_EDITOR
        if (AssignClipsFromCatalog())
        {
            return;
        }

        AssignClip(0, "Assets/DownloadedAnimations/Pointing.fbx");
        AssignClip(1, "Assets/DownloadedAnimations/Silly Dancing.fbx");
        AssignClip(2, "Assets/DownloadedAnimations/Hip Hop Dancing.fbx");
        AssignClip(3, "Assets/DownloadedAnimations/Breakdance Uprock Var 2.fbx");
        AssignClip(4, "Assets/DownloadedAnimations/Breakdance Freeze Var 2.fbx");
        AssignClip(5, "Assets/DownloadedAnimations/Standing Idle To Fight Idle.fbx");
        AssignClip(6, "Assets/DownloadedAnimations/Talking On Phone.fbx");
#endif
    }

#if UNITY_EDITOR
    private bool AssignClipsFromCatalog()
    {
        var catalogAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(CatalogPath);
        if (catalogAsset == null)
        {
            return false;
        }

        var catalog = JsonUtility.FromJson<HotkeyCatalog>(catalogAsset.text);
        if (catalog == null || catalog.animations == null || catalog.animations.Length == 0)
        {
            return false;
        }

        animations = new HotkeyAnimation[catalog.animations.Length];
        for (var i = 0; i < catalog.animations.Length; i++)
        {
            var item = catalog.animations[i];
            if (item == null || string.IsNullOrWhiteSpace(item.label) || !System.Enum.TryParse<Key>(item.key, true, out var key))
            {
                continue;
            }

            var path = !string.IsNullOrWhiteSpace(item.path)
                ? item.path
                : "Assets/DownloadedAnimations/" + item.file;

            animations[i] = new HotkeyAnimation
            {
                label = item.label,
                key = key,
                clip = LoadFirstAnimationClip(path),
                loop = item.loop
            };
        }

        return animations.Any(item => item != null && item.clip != null);
    }

    private AnimationClip LoadFirstAnimationClip(string path)
    {
        var clips = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
        for (var i = 0; i < clips.Length; i++)
        {
            if (clips[i] is AnimationClip clip && !clip.name.StartsWith("__preview__"))
            {
                return clip;
            }
        }

        return null;
    }

    private void AssignClip(int index, string path)
    {
        if (animations == null || index < 0 || index >= animations.Length || animations[index] == null)
        {
            return;
        }

        if (animations[index].clip != null) return;

        var clips = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
        for (var i = 0; i < clips.Length; i++)
        {
            if (clips[i] is AnimationClip clip && !clip.name.StartsWith("__preview__"))
            {
                animations[index].clip = clip;
                return;
            }
        }
    }

    [System.Serializable]
    private class HotkeyCatalog
    {
        public HotkeyCatalogEntry[] animations;
    }

    [System.Serializable]
    private class HotkeyCatalogEntry
    {
        public string label;
        public string key;
        public string file;
        public string path;
        public bool loop;
    }
#endif
}
