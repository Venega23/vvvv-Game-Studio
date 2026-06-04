using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public class CharacterActionRecorder : MonoBehaviour
{
    [Header("Keys")]
    public Key recordKey = Key.R;
    public Key stopTakeKey = Key.T;
    public Key playbackKey = Key.O;
    public Key ghostGuideKey = Key.H;
    public Key resetKey = Key.Backspace;
    public Key menuKey = Key.F3;
    public Key renderVideoKey = Key.F9;

    [Header("Limits")]
    public int maxRecordedCharacters = 4;
    public float maxRecordingSeconds = 300f;

    [Header("Video Export")]
    public int videoFrameRate = 30;
    public int jpegQuality = 85;

    [Header("Ghost Guide")]
    public bool ghostGuideEnabled = true;
    [Range(0.05f, 0.8f)]
    public float ghostGuideAlpha = 0.32f;

    public enum Mode { Standby, Recording, Playing, Rendering }

    [Serializable]
    public struct BoneSnapshot
    {
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale;
    }

    [Serializable]
    public struct FrameSnapshot
    {
        public float timestamp;
        public Vector3 position;
        public Quaternion rotation;
        public int animStateHash;
        public float animStateNormalizedTime;
        public float animSpeed;
        public float animMotionSpeed;
        public bool animGrounded;
        public bool animJump;
        public bool animFreeFall;
        public bool animStrutting;
        public BoneSnapshot[] bones;
    }

    public class CharacterRecording
    {
        public PlayerMove character;
        public readonly List<FrameSnapshot> frames = new();
        public float duration;
        public bool isPlaying;
        public float playbackTime;
        public Animator animator;
        public string[] bonePaths;
        public Transform[] boneTransforms;
        public CharacterController controller;
        public RumiStarterAssetsAnimatorDriver animDriver;
        public bool wasEnabled;
        public bool ccWasEnabled;
        public bool driverWasEnabled;

        public void Clear()
        {
            frames.Clear();
            duration = 0f;
            isPlaying = false;
            playbackTime = 0f;
        }
    }

    [Serializable]
    private class SavedTake
    {
        public string name;
        public string savedAt;
        public SavedRecording[] recordings;
    }

    [Serializable]
    private class SavedRecording
    {
        public string characterName;
        public float duration;
        public string[] bonePaths;
        public FrameSnapshot[] frames;
    }

    public static CharacterActionRecorder Instance { get; private set; }
    public static bool IsMenuOpen { get; private set; }
    public static bool IsRenderingVideo => Instance != null && Instance.isRenderingVideo;
    public Mode CurrentMode { get; private set; } = Mode.Standby;
    public PlayerMove RecordingTarget { get; private set; }

    // Public API for SimpleInventoryWindow integration
    public List<CharacterRecording> RecordingsList => recordings;
    public string[] SavedTakesList => savedTakePaths;
    public string TakeNameField { get => takeName; set => takeName = value; }
    public string StatusMessageText => statusMessage;
    public void PublicRefreshSavedTakes() => RefreshSavedTakes();
    public void PublicLoadTake(string path) => LoadTake(path);
    public void PublicRenderTake(string path) => RenderTake(path);
    public void PublicDeleteTake(string path) => DeleteTake(path);
    public void PublicDeleteMemoryRecording(int index) => DeleteMemoryRecording(index);
    public void PublicSaveCurrentTake() => SaveCurrentTake();
    public void PublicOpenRecordingsFolder() => OpenRecordingsFolder();
    public void PublicOpenVideosFolder() => OpenVideosFolder();

    private const int WindowId = 918733;
    private readonly List<CharacterRecording> recordings = new();
    private CharacterRecording activeRecording;
    private float recordStartTime;
    private Rect windowRect = new Rect(26f, 86f, 680f, 620f);
    private Vector2 memoryScroll;
    private Vector2 savedScroll;
    private Vector2 videoScroll;
    private int recorderTab;
    private string takeName = "Take 1";
    private string statusMessage = "Ready.";
    private string[] savedTakePaths = Array.Empty<string>();
    private string[] videoPaths = Array.Empty<string>();
    private double nextSavedRefreshTime;
    private double nextVideoRefreshTime;
    private bool isRenderingVideo;
    private bool cancelRenderingRequested;
    private bool stopRecordingAtEndOfFrame;
    private string lastVideoPath;
    private readonly List<RenderLockedCharacter> renderLocks = new();
    private readonly List<GhostPlayback> ghostPlaybacks = new();

    private int idSpeed;
    private int idMotionSpeed;
    private int idGrounded;
    private int idJump;
    private int idFreeFall;
    private int idStrutting;

    private GUIStyle windowStyle;
    private GUIStyle panelStyle;
    private GUIStyle buttonStyle;
    private GUIStyle activeButtonStyle;
    private GUIStyle tabStyle;
    private GUIStyle activeTabStyle;
    private GUIStyle labelStyle;
    private GUIStyle mutedStyle;
    private GUIStyle titleStyle;
    private GUIStyle hudBoxStyle;
    private GUIStyle hudLabelStyle;
    private GUIStyle hudAccentStyle;
    private GUIStyle hudMutedStyle;

    private class RenderLockedCharacter
    {
        public PlayerMove move;
        public CharacterController controller;
        public RumiStarterAssetsAnimatorDriver animDriver;
        public bool moveEnabled;
        public bool controllerEnabled;
        public bool driverEnabled;
    }

    private class GhostPlayback
    {
        public CharacterRecording source;
        public GameObject ghost;
        public Animator animator;
        public Transform[] boneTransforms;
        public float playbackTime;
        public Renderer[] sourceRenderers;
        public bool[] sourceRendererStates;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (FindAnyObjectByType<CharacterActionRecorder>() != null)
        {
            return;
        }

        var go = new GameObject("CharacterActionRecorder");
        DontDestroyOnLoad(go);
        go.AddComponent<CharacterActionRecorder>();
    }

    private void Awake()
    {
        Instance = this;
        CacheAnimatorIds();
        EnsureOutputFolders();
        RefreshSavedTakes();
        RefreshVideoFiles();
    }

    private void OnDestroy()
    {
        DestroyGhostGuide();
        if (Instance == this)
        {
            Instance = null;
            IsMenuOpen = false;
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
                SetMenuOpen(false);
            }
            return;
        }

        if (keyboard[menuKey].wasPressedThisFrame)
        {
            SetMenuOpen(!IsMenuOpen);
            return;
        }

        if (SimpleInventoryWindow.IsOpen || MapSwitcher.IsMenuOpen || IsMenuOpen || SurfaceItemPlacer.BlocksToolInput)
        {
            return;
        }

        if (keyboard[resetKey].wasPressedThisFrame)
        {
            StopCurrentOperation();
            return;
        }

        if (keyboard[ghostGuideKey].wasPressedThisFrame)
        {
            SetGhostGuideEnabled(!ghostGuideEnabled);
        }

        if (keyboard[stopTakeKey].wasPressedThisFrame)
        {
            if (CurrentMode == Mode.Recording)
            {
                stopRecordingAtEndOfFrame = true;
                SetStatus("Stopping take...");
            }
            else
            {
                SetStatus("No take is recording.");
            }
            return;
        }

        if (keyboard[recordKey].wasPressedThisFrame)
        {
            if (CurrentMode == Mode.Standby)
            {
                StartRecording();
            }
            else if (CurrentMode == Mode.Recording)
            {
                SetStatus("Already recording. Press T to stop this take.");
            }
            else
            {
                SetStatus("Stop playback/video export before recording.");
            }
        }

        if (keyboard[playbackKey].wasPressedThisFrame)
        {
            TogglePlayback();
        }

        if (keyboard[renderVideoKey].wasPressedThisFrame)
        {
            StartRenderVideo();
        }
    }

    private void LateUpdate()
    {
        if (CurrentMode == Mode.Recording && activeRecording != null)
        {
            RecordFrame(activeRecording);
            UpdateGhostGuide(Time.deltaTime);
            if (Time.time - recordStartTime >= maxRecordingSeconds)
            {
                stopRecordingAtEndOfFrame = true;
            }

            if (stopRecordingAtEndOfFrame)
            {
                StopRecording(false);
            }
        }
    }

    private void FixedUpdate()
    {
        if (CurrentMode != Mode.Playing && CurrentMode != Mode.Rendering)
        {
            return;
        }

        var anyStillPlaying = false;
        for (var i = 0; i < recordings.Count; i++)
        {
            var rec = recordings[i];
            if (!rec.isPlaying || rec.frames.Count == 0)
            {
                continue;
            }

            rec.playbackTime += Time.fixedDeltaTime;
            if (rec.playbackTime >= rec.duration)
            {
                ApplyFrame(rec, rec.frames[rec.frames.Count - 1]);
                rec.isPlaying = false;
            }
            else
            {
                ApplyInterpolatedFrame(rec);
                anyStillPlaying = true;
            }
        }

        if (!anyStillPlaying)
        {
            StopAllPlayback();
        }
    }

    public void ToggleRecording()
    {
        if (CurrentMode == Mode.Recording)
        {
            StopRecording();
        }
        else if (CurrentMode == Mode.Standby)
        {
            StartRecording();
        }
    }

    public void TogglePlayback()
    {
        if (isRenderingVideo)
        {
            SetStatus("Video export is running. Use Stop current to cancel it.");
            return;
        }

        if (CurrentMode == Mode.Playing)
        {
            StopAllPlayback();
        }
        else if (CurrentMode == Mode.Standby && GetReadyCount() > 0)
        {
            StartAllPlayback();
        }
    }

    public void StartRecording()
    {
        if (isRenderingVideo)
        {
            SetStatus("Wait until video export finishes.");
            return;
        }

        var character = FindActiveCharacter();
        if (character == null)
        {
            SetStatus("No active character to record.");
            return;
        }

        StopCharacterAnimationOverrides(character, true);
        var rec = FindRecording(character);
        if (rec == null)
        {
            if (recordings.Count >= maxRecordedCharacters)
            {
                SetStatus("Maximum recordings reached. Reset or delete one first.");
                return;
            }

            rec = new CharacterRecording { character = character };
            recordings.Add(rec);
        }

        WireRecordingReferences(rec);
        rec.Clear();
        activeRecording = rec;
        RecordingTarget = character;
        recordStartTime = Time.time;
        stopRecordingAtEndOfFrame = false;
        CurrentMode = Mode.Recording;
        StartGhostGuide(character);
        SetStatus("Recording " + CleanName(character.name) + ".");
    }

    public void StopRecording()
    {
        StopRecording(true);
    }

    private void StopRecording(bool captureFinalFrame)
    {
        if (activeRecording == null)
        {
            RecordingTarget = null;
            if (CurrentMode == Mode.Recording)
            {
                CurrentMode = Mode.Standby;
                DestroyGhostGuide();
                SetStatus("Recording stopped.");
            }
            return;
        }

        if (captureFinalFrame)
        {
            RecordFrame(activeRecording, true);
        }

        activeRecording.duration = activeRecording.frames.Count > 0
            ? activeRecording.frames[activeRecording.frames.Count - 1].timestamp
            : 0f;

        SetStatus("Recorded " + CleanName(activeRecording.character.name) + ": " + activeRecording.duration.ToString("0.0") + "s.");
        activeRecording = null;
        RecordingTarget = null;
        stopRecordingAtEndOfFrame = false;
        CurrentMode = Mode.Standby;
        DestroyGhostGuide();
    }

    private void SetGhostGuideEnabled(bool enabled)
    {
        ghostGuideEnabled = enabled;
        if (!ghostGuideEnabled)
        {
            DestroyGhostGuide();
        }
        else if (CurrentMode == Mode.Recording && RecordingTarget != null)
        {
            StartGhostGuide(RecordingTarget);
        }

        SetStatus("Ghost guide " + (ghostGuideEnabled ? "enabled." : "disabled."));
    }

    private void StartGhostGuide(PlayerMove excludeCharacter)
    {
        DestroyGhostGuide();
        if (!ghostGuideEnabled)
        {
            return;
        }

        for (var i = 0; i < recordings.Count; i++)
        {
            var rec = recordings[i];
            if (rec == null || rec.character == null || rec.character == excludeCharacter || rec.frames.Count == 0)
            {
                continue;
            }

            WireRecordingReferences(rec);
            var ghost = CreateGhostClone(rec);
            if (ghost == null)
            {
                continue;
            }

            var item = new GhostPlayback
            {
                source = rec,
                ghost = ghost,
                animator = ghost.GetComponentInChildren<Animator>(true),
                sourceRenderers = rec.character.GetComponentsInChildren<Renderer>(true)
            };
            item.boneTransforms = ResolveBoneTransforms(item.animator != null ? item.animator.transform : ghost.transform, rec.bonePaths);

            item.sourceRendererStates = new bool[item.sourceRenderers.Length];
            for (var r = 0; r < item.sourceRenderers.Length; r++)
            {
                var renderer = item.sourceRenderers[r];
                item.sourceRendererStates[r] = renderer != null && renderer.enabled;
                if (renderer != null)
                {
                    renderer.enabled = false;
                }
            }

            ApplyGhostFrame(item, rec.frames[0]);
            ghostPlaybacks.Add(item);
        }
    }

    private GameObject CreateGhostClone(CharacterRecording rec)
    {
        if (rec.character == null)
        {
            return null;
        }

        var ghost = Instantiate(rec.character.gameObject, rec.frames[0].position, rec.frames[0].rotation);
        ghost.name = "GhostGuide_" + CleanName(rec.character.name);

        var behaviours = ghost.GetComponentsInChildren<MonoBehaviour>(true);
        for (var i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] != null)
            {
                behaviours[i].enabled = false;
            }
        }

        var colliders = ghost.GetComponentsInChildren<Collider>(true);
        for (var i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
            {
                colliders[i].enabled = false;
            }
        }

        var bodies = ghost.GetComponentsInChildren<Rigidbody>(true);
        for (var i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] != null)
            {
                bodies[i].isKinematic = true;
                bodies[i].detectCollisions = false;
            }
        }

        var renderers = ghost.GetComponentsInChildren<Renderer>(true);
        for (var i = 0; i < renderers.Length; i++)
        {
            ConfigureGhostRenderer(renderers[i]);
        }

        return ghost;
    }

    private void ConfigureGhostRenderer(Renderer renderer)
    {
        if (renderer == null)
        {
            return;
        }

        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        var materials = renderer.materials;
        for (var i = 0; i < materials.Length; i++)
        {
            ConfigureGhostMaterial(materials[i]);
        }
    }

    private void ConfigureGhostMaterial(Material material)
    {
        if (material == null)
        {
            return;
        }

        var color = new Color(0.55f, 0.9f, 1f, Mathf.Clamp01(ghostGuideAlpha));
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend")) material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private void UpdateGhostGuide(float deltaTime)
    {
        for (var i = ghostPlaybacks.Count - 1; i >= 0; i--)
        {
            var item = ghostPlaybacks[i];
            if (item == null || item.ghost == null || item.source == null || item.source.frames.Count == 0)
            {
                ghostPlaybacks.RemoveAt(i);
                continue;
            }

            item.playbackTime += deltaTime;
            var frames = item.source.frames;
            var last = frames[frames.Count - 1];
            if (item.playbackTime >= item.source.duration)
            {
                ApplyGhostFrame(item, last);
                continue;
            }

            ApplyGhostFrame(item, GetInterpolatedFrame(frames, item.playbackTime));
        }
    }

    private FrameSnapshot GetInterpolatedFrame(List<FrameSnapshot> frames, float time)
    {
        if (frames == null || frames.Count == 0)
        {
            return default;
        }

        if (frames.Count == 1 || time <= frames[0].timestamp)
        {
            return frames[0];
        }

        var lo = 0;
        var hi = frames.Count - 1;
        while (lo < hi - 1)
        {
            var mid = (lo + hi) / 2;
            if (frames[mid].timestamp <= time) lo = mid;
            else hi = mid;
        }

        var a = frames[lo];
        var b = frames[hi];
        var range = b.timestamp - a.timestamp;
        var blend = range > 0.0001f ? Mathf.Clamp01((time - a.timestamp) / range) : 0f;
        var result = blend < 0.5f ? a : b;
        result.timestamp = time;
        result.position = Vector3.Lerp(a.position, b.position, blend);
        result.rotation = Quaternion.Slerp(a.rotation, b.rotation, blend);
        result.bones = InterpolateBones(a.bones, b.bones, blend);
        return result;
    }

    private BoneSnapshot[] InterpolateBones(BoneSnapshot[] a, BoneSnapshot[] b, float blend)
    {
        if (a == null || b == null || a.Length == 0 || a.Length != b.Length)
        {
            return a ?? b;
        }

        var result = new BoneSnapshot[a.Length];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new BoneSnapshot
            {
                localPosition = Vector3.Lerp(a[i].localPosition, b[i].localPosition, blend),
                localRotation = Quaternion.Slerp(a[i].localRotation, b[i].localRotation, blend),
                localScale = Vector3.Lerp(a[i].localScale, b[i].localScale, blend)
            };
        }

        return result;
    }

    private void ApplyGhostFrame(GhostPlayback item, FrameSnapshot snap)
    {
        if (item == null || item.ghost == null)
        {
            return;
        }

        item.ghost.transform.SetPositionAndRotation(snap.position, snap.rotation);
        SetAnimatorState(item.animator, snap);
        if (item.animator != null)
        {
            item.animator.Update(0f);
        }
        ApplyBoneSnapshot(item.boneTransforms, snap.bones);
    }

    private void DestroyGhostGuide()
    {
        for (var i = 0; i < ghostPlaybacks.Count; i++)
        {
            var item = ghostPlaybacks[i];
            if (item == null)
            {
                continue;
            }

            if (item.sourceRenderers != null && item.sourceRendererStates != null)
            {
                var count = Mathf.Min(item.sourceRenderers.Length, item.sourceRendererStates.Length);
                for (var r = 0; r < count; r++)
                {
                    if (item.sourceRenderers[r] != null)
                    {
                        item.sourceRenderers[r].enabled = item.sourceRendererStates[r];
                    }
                }
            }

            if (item.ghost != null)
            {
                Destroy(item.ghost);
            }
        }

        ghostPlaybacks.Clear();
    }

    public void StartAllPlayback()
    {
        if (isRenderingVideo && CurrentMode != Mode.Standby)
        {
            return;
        }

        var started = 0;
        for (var i = 0; i < recordings.Count; i++)
        {
            var rec = recordings[i];
            if (rec.frames.Count == 0 || rec.character == null)
            {
                continue;
            }

            WireRecordingReferences(rec);
            PrepareRecordingForPlayback(rec);
            rec.wasEnabled = rec.character.enabled;
            rec.ccWasEnabled = rec.controller != null && rec.controller.enabled;
            rec.driverWasEnabled = rec.animDriver != null && rec.animDriver.enabled;

            rec.character.ClearInputState();
            rec.character.enabled = false;
            if (rec.controller != null) rec.controller.enabled = false;
            if (rec.animDriver != null) rec.animDriver.enabled = false;

            rec.playbackTime = 0f;
            rec.isPlaying = true;
            ApplyFrame(rec, rec.frames[0]);
            started++;
        }

        if (started > 0)
        {
            CurrentMode = Mode.Playing;
            SetStatus("Playing " + started + " recording(s).");
        }
    }

    private void LockAllCharactersForRender()
    {
        RumiDownloadedAnimationHotkeys.StopAll(true);
        renderLocks.Clear();
        var allMoves = FindObjectsByType<PlayerMove>(FindObjectsInactive.Include);
        for (var i = 0; i < allMoves.Length; i++)
        {
            var move = allMoves[i];
            if (move == null)
            {
                continue;
            }

            var controller = move.GetComponent<CharacterController>();
            var driver = move.GetComponentInChildren<RumiStarterAssetsAnimatorDriver>(true);
            renderLocks.Add(new RenderLockedCharacter
            {
                move = move,
                controller = controller,
                animDriver = driver,
                moveEnabled = move.enabled,
                controllerEnabled = controller != null && controller.enabled,
                driverEnabled = driver != null && driver.enabled
            });

            move.ClearInputState();
            move.enabled = false;
            if (controller != null) controller.enabled = false;
            if (driver != null) driver.enabled = false;
        }
    }

    private void PrepareRecordingForPlayback(CharacterRecording rec)
    {
        if (rec == null || rec.character == null)
        {
            return;
        }

        EnsureBoneCache(rec);
        StopCharacterAnimationOverrides(rec.character, true);
        if (rec.animator != null)
        {
            rec.animator.Rebind();
            rec.animator.Update(0f);
            if (rec.frames.Count > 0)
            {
                SetAnimatorState(rec.animator, rec.frames[0]);
                rec.animator.Update(0f);
            }
        }
    }

    private void StopCharacterAnimationOverrides(PlayerMove character, bool resetAnimatorPose)
    {
        if (character == null)
        {
            return;
        }

        var hotkeys = character.GetComponentsInChildren<RumiDownloadedAnimationHotkeys>(true);
        for (var i = 0; i < hotkeys.Length; i++)
        {
            if (hotkeys[i] != null)
            {
                hotkeys[i].StopAnimation(false, resetAnimatorPose);
            }
        }
    }

    private void RestoreRenderLocks()
    {
        for (var i = 0; i < renderLocks.Count; i++)
        {
            var item = renderLocks[i];
            if (item.move == null)
            {
                continue;
            }

            item.move.enabled = item.moveEnabled;
            if (item.controller != null) item.controller.enabled = item.controllerEnabled;
            if (item.animDriver != null) item.animDriver.enabled = item.driverEnabled;
        }
        renderLocks.Clear();
    }

    public void StopAllPlayback()
    {
        for (var i = 0; i < recordings.Count; i++)
        {
            var rec = recordings[i];
            if (rec.character == null)
            {
                continue;
            }

            rec.isPlaying = false;
            rec.character.enabled = rec.wasEnabled;
            if (rec.controller != null) rec.controller.enabled = rec.ccWasEnabled;
            if (rec.animDriver != null) rec.animDriver.enabled = rec.driverWasEnabled;
        }

        CurrentMode = Mode.Standby;
        SetStatus("Playback stopped.");
    }

    private void StopPlaybackForRender()
    {
        for (var i = 0; i < recordings.Count; i++)
        {
            var rec = recordings[i];
            rec.isPlaying = false;
        }

        CurrentMode = Mode.Standby;
    }

    public void StopCurrentOperation()
    {
        if (isRenderingVideo || CurrentMode == Mode.Rendering)
        {
            cancelRenderingRequested = true;
            StopAllPlayback();
            SetStatus("Video export canceled.");
            return;
        }

        if (CurrentMode == Mode.Recording)
        {
            StopRecording();
            return;
        }

        if (CurrentMode == Mode.Playing)
        {
            StopAllPlayback();
            return;
        }

        SetStatus("Nothing is running.");
    }

    public void ResetAll()
    {
        if (CurrentMode == Mode.Recording)
        {
            StopRecording();
        }
        if (CurrentMode == Mode.Playing)
        {
            StopAllPlayback();
        }

        recordings.Clear();
        CurrentMode = Mode.Standby;
        SetStatus("All in-memory recordings cleared.");
    }

    public int GetReadyCount()
    {
        var count = 0;
        for (var i = 0; i < recordings.Count; i++)
        {
            if (recordings[i].frames.Count > 0)
            {
                count++;
            }
        }
        return count;
    }

    private void RecordFrame(CharacterRecording rec)
    {
        RecordFrame(rec, false);
    }

    private void RecordFrame(CharacterRecording rec, bool force)
    {
        if (rec.character == null)
        {
            return;
        }

        EnsureBoneCache(rec);
        var timestamp = Time.time - recordStartTime;
        if (!force && rec.frames.Count > 0 && timestamp <= rec.frames[rec.frames.Count - 1].timestamp + 0.0001f)
        {
            return;
        }

        var t = rec.character.transform;
        var state = rec.animator != null ? rec.animator.GetCurrentAnimatorStateInfo(0) : default;
        var snapshot = new FrameSnapshot
        {
            timestamp = timestamp,
            position = t.position,
            rotation = t.rotation,
            animStateHash = state.fullPathHash,
            animStateNormalizedTime = rec.animator != null ? state.normalizedTime : 0f,
            animSpeed = GetAnimFloat(rec.animator, idSpeed),
            animMotionSpeed = GetAnimFloat(rec.animator, idMotionSpeed),
            animGrounded = GetAnimBool(rec.animator, idGrounded),
            animJump = GetAnimBool(rec.animator, idJump),
            animFreeFall = GetAnimBool(rec.animator, idFreeFall),
            animStrutting = GetAnimBool(rec.animator, idStrutting),
            bones = CaptureBoneSnapshot(rec.boneTransforms)
        };

        if (force && rec.frames.Count > 0 && timestamp <= rec.frames[rec.frames.Count - 1].timestamp + 0.0001f)
        {
            snapshot.timestamp = rec.frames[rec.frames.Count - 1].timestamp + 0.0001f;
        }

        rec.frames.Add(snapshot);
    }

    private void ApplyInterpolatedFrame(CharacterRecording rec)
    {
        var frames = rec.frames;
        var t = rec.playbackTime;
        var lo = 0;
        var hi = frames.Count - 1;
        while (lo < hi - 1)
        {
            var mid = (lo + hi) / 2;
            if (frames[mid].timestamp <= t) lo = mid;
            else hi = mid;
        }

        if (lo >= hi)
        {
            ApplyFrame(rec, frames[lo]);
            return;
        }

        var a = frames[lo];
        var b = frames[hi];
        var range = b.timestamp - a.timestamp;
        var blend = range > 0.0001f ? Mathf.Clamp01((t - a.timestamp) / range) : 0f;
        rec.character.transform.SetPositionAndRotation(
            Vector3.Lerp(a.position, b.position, blend),
            Quaternion.Slerp(a.rotation, b.rotation, blend));

        SetAnimatorState(rec.animator, blend < 0.5f ? a : b);
        if (rec.animator != null)
        {
            rec.animator.Update(0f);
        }
        ApplyBoneSnapshot(rec.boneTransforms, InterpolateBones(a.bones, b.bones, blend));
    }

    private void ApplyFrame(CharacterRecording rec, FrameSnapshot snap)
    {
        if (rec.character == null)
        {
            return;
        }

        rec.character.transform.SetPositionAndRotation(snap.position, snap.rotation);
        SetAnimatorState(rec.animator, snap);
        if (rec.animator != null)
        {
            rec.animator.Update(0f);
        }
        ApplyBoneSnapshot(rec.boneTransforms, snap.bones);
    }

    private void SetAnimatorState(Animator animator, FrameSnapshot snap)
    {
        if (animator == null)
        {
            return;
        }

        if (snap.animStateHash != 0)
        {
            animator.Play(snap.animStateHash, 0, snap.animStateNormalizedTime);
        }
        animator.SetFloat(idSpeed, snap.animSpeed);
        animator.SetFloat(idMotionSpeed, snap.animMotionSpeed);
        animator.SetBool(idGrounded, snap.animGrounded);
        animator.SetBool(idJump, snap.animJump);
        animator.SetBool(idFreeFall, snap.animFreeFall);
        animator.SetBool(idStrutting, snap.animStrutting);
    }

    private void EnsureBoneCache(CharacterRecording rec)
    {
        if (rec == null)
        {
            return;
        }

        if (rec.animator == null)
        {
            WireRecordingReferences(rec);
        }

        if (rec.animator == null)
        {
            rec.bonePaths = Array.Empty<string>();
            rec.boneTransforms = Array.Empty<Transform>();
            return;
        }

        if (rec.bonePaths == null || rec.bonePaths.Length == 0)
        {
            rec.bonePaths = BuildBonePaths(rec.animator.transform);
        }

        if (rec.boneTransforms == null || rec.boneTransforms.Length != rec.bonePaths.Length)
        {
            rec.boneTransforms = ResolveBoneTransforms(rec.animator.transform, rec.bonePaths);
        }
    }

    private string[] BuildBonePaths(Transform animatorRoot)
    {
        if (animatorRoot == null)
        {
            return Array.Empty<string>();
        }

        var paths = new List<string>();
        for (var i = 0; i < animatorRoot.childCount; i++)
        {
            CollectBonePaths(animatorRoot.GetChild(i), animatorRoot, paths);
        }

        return paths.ToArray();
    }

    private void CollectBonePaths(Transform item, Transform root, List<string> paths)
    {
        if (item == null || root == null)
        {
            return;
        }

        paths.Add(GetRelativePath(root, item));
        for (var i = 0; i < item.childCount; i++)
        {
            CollectBonePaths(item.GetChild(i), root, paths);
        }
    }

    private string GetRelativePath(Transform root, Transform item)
    {
        var stack = new Stack<string>();
        var current = item;
        while (current != null && current != root)
        {
            stack.Push(current.name);
            current = current.parent;
        }

        return string.Join("/", stack);
    }

    private Transform[] ResolveBoneTransforms(Transform root, string[] paths)
    {
        if (root == null || paths == null || paths.Length == 0)
        {
            return Array.Empty<Transform>();
        }

        var transforms = new Transform[paths.Length];
        for (var i = 0; i < paths.Length; i++)
        {
            transforms[i] = string.IsNullOrEmpty(paths[i]) ? root : root.Find(paths[i]);
        }

        return transforms;
    }

    private BoneSnapshot[] CaptureBoneSnapshot(Transform[] bones)
    {
        if (bones == null || bones.Length == 0)
        {
            return Array.Empty<BoneSnapshot>();
        }

        var snapshot = new BoneSnapshot[bones.Length];
        for (var i = 0; i < bones.Length; i++)
        {
            var bone = bones[i];
            if (bone == null)
            {
                continue;
            }

            snapshot[i] = new BoneSnapshot
            {
                localPosition = bone.localPosition,
                localRotation = bone.localRotation,
                localScale = bone.localScale
            };
        }

        return snapshot;
    }

    private void ApplyBoneSnapshot(Transform[] bones, BoneSnapshot[] snapshot)
    {
        if (bones == null || snapshot == null)
        {
            return;
        }

        var count = Mathf.Min(bones.Length, snapshot.Length);
        for (var i = 0; i < count; i++)
        {
            var bone = bones[i];
            if (bone == null)
            {
                continue;
            }

            bone.localPosition = snapshot[i].localPosition;
            bone.localRotation = snapshot[i].localRotation;
            bone.localScale = snapshot[i].localScale;
        }
    }

    private void SaveCurrentTake()
    {
        if (GetReadyCount() == 0)
        {
            SetStatus("Nothing to save yet.");
            return;
        }

        var saved = new List<SavedRecording>();
        for (var i = 0; i < recordings.Count; i++)
        {
            var rec = recordings[i];
            if (rec.frames.Count == 0 || rec.character == null)
            {
                continue;
            }

            saved.Add(new SavedRecording
            {
                characterName = CleanName(rec.character.name),
                duration = rec.duration,
                bonePaths = rec.bonePaths,
                frames = rec.frames.ToArray()
            });
        }

        var take = new SavedTake
        {
            name = string.IsNullOrWhiteSpace(takeName) ? "Take" : takeName.Trim(),
            savedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            recordings = saved.ToArray()
        };

        Directory.CreateDirectory(GetRecordingsDirectory());
        var fileName = SanitizeFileName(take.name) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json";
        var path = Path.Combine(GetRecordingsDirectory(), fileName);
        File.WriteAllText(path, JsonUtility.ToJson(take, true));
        RefreshSavedTakes();
        SetStatus("Saved " + take.name + ".");
    }

    private void LoadTake(string path)
    {
        if (!File.Exists(path))
        {
            SetStatus("Take file not found.");
            return;
        }

        if (CurrentMode == Mode.Playing)
        {
            StopAllPlayback();
        }
        if (CurrentMode == Mode.Recording)
        {
            StopRecording();
        }

        var json = File.ReadAllText(path);
        var take = JsonUtility.FromJson<SavedTake>(json);
        if (take == null || take.recordings == null || take.recordings.Length == 0)
        {
            SetStatus("Take is empty.");
            return;
        }

        recordings.Clear();
        var loaded = 0;
        for (var i = 0; i < take.recordings.Length; i++)
        {
            var saved = take.recordings[i];
            var character = FindCharacterByName(saved.characterName);
            if (character == null || saved.frames == null || saved.frames.Length == 0)
            {
                continue;
            }

            var rec = new CharacterRecording
            {
                character = character,
                duration = saved.duration,
                bonePaths = saved.bonePaths
            };
            rec.frames.AddRange(saved.frames);
            WireRecordingReferences(rec);
            EnsureBoneCache(rec);
            recordings.Add(rec);
            loaded++;
        }

        takeName = string.IsNullOrWhiteSpace(take.name) ? Path.GetFileNameWithoutExtension(path) : take.name;
        SetStatus("Loaded " + loaded + " recording(s) from " + takeName + ".");
    }

    private void DeleteTake(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        RefreshSavedTakes();
        SetStatus("Deleted saved take.");
    }

    private void RenderTake(string path)
    {
        if (isRenderingVideo)
        {
            SetStatus("Video export is already running.");
            return;
        }

        LoadTake(path);
        if (GetReadyCount() > 0)
        {
            StartRenderVideo();
        }
    }

    private void DeleteVideo(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
        if (lastVideoPath == path)
        {
            lastVideoPath = null;
        }
        RefreshVideoFiles();
        SetStatus("Deleted video.");
    }

    private void StartRenderVideo()
    {
        if (isRenderingVideo)
        {
            SetStatus("Video export is already running.");
            return;
        }

        if (CurrentMode == Mode.Recording)
        {
            StopRecording();
        }
        else if (CurrentMode == Mode.Playing)
        {
            StopAllPlayback();
        }

        if (CurrentMode != Mode.Standby)
        {
            SetStatus("Stop recording/playback before video export.");
            return;
        }

        if (GetReadyCount() == 0)
        {
            SetStatus("Record or load a take before video export.");
            return;
        }

        StartCoroutine(RenderVideoCoroutine());
    }

    private IEnumerator RenderVideoCoroutine()
    {
        isRenderingVideo = true;
        cancelRenderingRequested = false;
        SetMenuOpen(false);
        yield return null;

        Directory.CreateDirectory(GetVideosDirectory());
        var safeName = SanitizeFileName(string.IsNullOrWhiteSpace(takeName) ? "Take" : takeName);
        var videoPath = Path.Combine(GetVideosDirectory(), safeName + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".avi");
        SimpleAviWriter writer = null;
        var capturedFrames = 0;
        var nextCaptureTime = Time.unscaledTime;

        SetStatus("Rendering AVI video...");
        LockAllCharactersForRender();
        StartAllPlayback();
        if (CurrentMode != Mode.Playing)
        {
            RestoreRenderLocks();
            isRenderingVideo = false;
            SetStatus("Video export failed: playback could not start.");
            yield break;
        }

        CurrentMode = Mode.Rendering;

        while (CurrentMode == Mode.Rendering && !cancelRenderingRequested)
        {
            yield return new WaitForEndOfFrame();

            if (Time.unscaledTime + 0.0001f < nextCaptureTime)
            {
                continue;
            }

            var texture = ScreenCapture.CaptureScreenshotAsTexture();
            if (texture == null)
            {
                continue;
            }

            if (writer == null)
            {
                writer = new SimpleAviWriter(videoPath, texture.width, texture.height, Mathf.Max(1, videoFrameRate));
            }

            writer.AddFrame(texture.EncodeToJPG(Mathf.Clamp(jpegQuality, 1, 100)));
            Destroy(texture);
            capturedFrames++;
            nextCaptureTime += 1f / Mathf.Max(1, videoFrameRate);
        }

        writer?.Dispose();
        isRenderingVideo = false;
        StopPlaybackForRender();
        RestoreRenderLocks();

        if (cancelRenderingRequested)
        {
            if (File.Exists(videoPath))
            {
                File.Delete(videoPath);
            }
            CurrentMode = Mode.Standby;
            RefreshVideoFiles();
            SetStatus("Video export canceled.");
        }
        else if (capturedFrames > 0 && File.Exists(videoPath))
        {
            lastVideoPath = videoPath;
            CurrentMode = Mode.Standby;
            RefreshVideoFiles();
            SetStatus("Video saved: " + videoPath);
            OpenVideosFolder();
        }
        else
        {
            CurrentMode = Mode.Standby;
            SetStatus("Video export failed: no frames captured.");
        }
    }

    private void RefreshSavedTakes()
    {
        Directory.CreateDirectory(GetRecordingsDirectory());
        savedTakePaths = Directory.GetFiles(GetRecordingsDirectory(), "*.json");
        Array.Sort(savedTakePaths);
        Array.Reverse(savedTakePaths);
        nextSavedRefreshTime = Time.unscaledTimeAsDouble + 2d;
    }

    private void RefreshVideoFiles()
    {
        Directory.CreateDirectory(GetVideosDirectory());
        videoPaths = Directory.GetFiles(GetVideosDirectory(), "*.avi");
        Array.Sort(videoPaths);
        Array.Reverse(videoPaths);
        nextVideoRefreshTime = Time.unscaledTimeAsDouble + 2d;
    }

    private void CacheAnimatorIds()
    {
        idSpeed = Animator.StringToHash("Speed");
        idMotionSpeed = Animator.StringToHash("MotionSpeed");
        idGrounded = Animator.StringToHash("Grounded");
        idJump = Animator.StringToHash("Jump");
        idFreeFall = Animator.StringToHash("FreeFall");
        idStrutting = Animator.StringToHash("Strutting");
    }

    private void WireRecordingReferences(CharacterRecording rec)
    {
        if (rec == null || rec.character == null)
        {
            return;
        }

        rec.animator = rec.character.GetComponentInChildren<Animator>(true);
        rec.controller = rec.character.GetComponent<CharacterController>();
        rec.animDriver = rec.character.GetComponentInChildren<RumiStarterAssetsAnimatorDriver>(true);
        if (rec.animator != null && rec.bonePaths != null && rec.bonePaths.Length > 0)
        {
            rec.boneTransforms = ResolveBoneTransforms(rec.animator.transform, rec.bonePaths);
        }
    }

    private PlayerMove FindActiveCharacter()
    {
        var allMoves = FindObjectsByType<PlayerMove>(FindObjectsInactive.Exclude);
        for (var i = 0; i < allMoves.Length; i++)
        {
            var move = allMoves[i];
            if (move.enabled && move.gameObject.activeInHierarchy)
            {
                return move;
            }
        }
        return null;
    }

    private PlayerMove FindCharacterByName(string characterName)
    {
        var cleanTarget = CleanName(characterName);
        var allMoves = FindObjectsByType<PlayerMove>(FindObjectsInactive.Include);
        for (var i = 0; i < allMoves.Length; i++)
        {
            if (CleanName(allMoves[i].name) == cleanTarget)
            {
                return allMoves[i];
            }
        }
        return null;
    }

    private CharacterRecording FindRecording(PlayerMove character)
    {
        for (var i = 0; i < recordings.Count; i++)
        {
            if (recordings[i].character == character)
            {
                return recordings[i];
            }
        }
        return null;
    }

    private void DeleteMemoryRecording(int index)
    {
        if (index < 0 || index >= recordings.Count)
        {
            return;
        }

        recordings.RemoveAt(index);
        SetStatus("Removed in-memory recording.");
    }

    private void AutoSwitchToNextCharacter()
    {
        if (FindAnyObjectByType<CharacterSwitcher>() == null)
        {
            return;
        }

        var allMoves = FindObjectsByType<PlayerMove>(FindObjectsInactive.Include);
        PlayerMove next = null;
        for (var i = 0; i < allMoves.Length; i++)
        {
            var move = allMoves[i];
            if (move.gameObject.activeInHierarchy && FindRecording(move) == null)
            {
                next = move;
                break;
            }
        }

        if (next == null)
        {
            return;
        }

        for (var i = 0; i < allMoves.Length; i++)
        {
            var move = allMoves[i];
            if (!move.gameObject.activeInHierarchy)
            {
                continue;
            }

            move.ClearInputState();
            move.enabled = false;
            var cc = move.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
        }

        next.enabled = true;
        var nextCC = next.GetComponent<CharacterController>();
        if (nextCC != null) nextCC.enabled = true;
        if (Camera.main != null)
        {
            next.cameraTransform = Camera.main.transform;
            var cam = Camera.main.GetComponent<CameraFollow>();
            if (cam != null)
            {
                cam.SetControlledTarget(next.transform, false);
            }
        }
    }

    private void OnGUI()
    {
        InitStyles();
        DrawHud();
        if (IsMenuOpen)
        {
            GUI.depth = -60;
            DrawRecorderPanel();
        }
    }

    private Rect GetPanelRect()
    {
        var width = Mathf.Min(720f, Screen.width - 24f);
        var height = Mathf.Min(650f, Screen.height - 24f);
        return new Rect(12f, 12f, width, height);
    }

    private void DrawRecorderPanel()
    {
        var rect = GetPanelRect();
        GUI.Box(rect, "Studio Recorder", windowStyle);
        GUILayout.BeginArea(new Rect(rect.x + 14f, rect.y + 30f, rect.width - 28f, rect.height - 44f));
        DrawRecorderContent();
        GUILayout.EndArea();
    }

    private void DrawRecorderContent()
    {
        GUILayout.Space(4f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Record", recorderTab == 0 ? activeTabStyle : tabStyle, GUILayout.Height(30f))) recorderTab = 0;
        if (GUILayout.Button("Takes", recorderTab == 1 ? activeTabStyle : tabStyle, GUILayout.Height(30f))) recorderTab = 1;
        if (GUILayout.Button("Videos", recorderTab == 2 ? activeTabStyle : tabStyle, GUILayout.Height(30f))) recorderTab = 2;
        if (GUILayout.Button("Settings", recorderTab == 3 ? activeTabStyle : tabStyle, GUILayout.Height(30f))) recorderTab = 3;
        GUILayout.EndHorizontal();
        GUILayout.Space(10f);

        if (recorderTab == 0)
        {
            DrawLiveTab();
        }
        else if (recorderTab == 1)
        {
            DrawSavedTab();
        }
        else if (recorderTab == 2)
        {
            DrawVideosTab();
        }
        else
        {
            DrawSettingsTab();
        }

        GUILayout.FlexibleSpace();
        GUILayout.Label(statusMessage, mutedStyle);
        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Close", activeButtonStyle, GUILayout.Width(96f), GUILayout.Height(30f)))
        {
            SetMenuOpen(false);
        }
        GUILayout.EndHorizontal();
    }

    private void DrawLiveTab()
    {
        GUILayout.BeginVertical(panelStyle);
        GUILayout.Label("Mode: " + GetModeLabel(), titleStyle);
        GUILayout.Label("Keys: R start take, T stop take, H ghost guide, O play, F9 video", mutedStyle);
        GUILayout.Label("Max take length: " + FormatDuration(maxRecordingSeconds), mutedStyle);
        GUILayout.Space(8f);
        GUILayout.BeginHorizontal();

        GUI.enabled = !isRenderingVideo && (CurrentMode == Mode.Standby || CurrentMode == Mode.Recording);
        if (GUILayout.Button(CurrentMode == Mode.Recording ? "Stop take" : "Record active", CurrentMode == Mode.Recording ? activeButtonStyle : buttonStyle, GUILayout.Height(34f)))
        {
            ToggleRecording();
        }

        GUI.enabled = CurrentMode != Mode.Standby || isRenderingVideo;
        if (GUILayout.Button("Stop current", activeButtonStyle, GUILayout.Height(34f)))
        {
            StopCurrentOperation();
        }

        GUI.enabled = !isRenderingVideo && (CurrentMode == Mode.Standby && GetReadyCount() > 0 || CurrentMode == Mode.Playing);
        if (GUILayout.Button(CurrentMode == Mode.Playing ? "Stop playback" : "Play all", CurrentMode == Mode.Playing ? activeButtonStyle : buttonStyle, GUILayout.Height(34f)))
        {
            TogglePlayback();
        }

        GUI.enabled = !isRenderingVideo && CurrentMode == Mode.Standby && recordings.Count > 0;
        if (GUILayout.Button("Reset", buttonStyle, GUILayout.Height(34f)))
        {
            ResetAll();
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        GUI.enabled = !isRenderingVideo;
        if (GUILayout.Button("Ghost guide: " + (ghostGuideEnabled ? "ON" : "OFF"), ghostGuideEnabled ? activeButtonStyle : buttonStyle, GUILayout.Height(30f)))
        {
            SetGhostGuideEnabled(!ghostGuideEnabled);
        }
        GUI.enabled = true;
        GUILayout.Label(ghostGuideEnabled ? "Previous takes appear as transparent timing guides while recording." : "Normal recording: no transparent guide playback.", mutedStyle);
        GUILayout.EndHorizontal();

        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        GUI.enabled = !isRenderingVideo && GetReadyCount() > 0;
        if (GUILayout.Button("Render AVI video", activeButtonStyle, GUILayout.Height(32f)))
        {
            StartRenderVideo();
        }
        GUI.enabled = true;
        if (GUILayout.Button("Open videos", buttonStyle, GUILayout.Width(104f), GUILayout.Height(32f)))
        {
            OpenVideosFolder();
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.Label("Video output: project/StudioVideos", mutedStyle);
        GUILayout.EndVertical();

        GUILayout.Space(10f);
        GUILayout.Label("In Memory", titleStyle);
        memoryScroll = GUILayout.BeginScrollView(memoryScroll, GUILayout.Height(260f));
        if (recordings.Count == 0)
        {
            GUILayout.Label("No recordings yet.", mutedStyle);
        }
        for (var i = 0; i < recordings.Count; i++)
        {
            DrawMemoryRecordingRow(i, recordings[i]);
        }
        GUILayout.EndScrollView();
    }

    private void DrawMemoryRecordingRow(int index, CharacterRecording rec)
    {
        GUILayout.BeginHorizontal(panelStyle);
        var name = rec.character != null ? CleanName(rec.character.name) : "Missing character";
        GUILayout.Label(name, labelStyle, GUILayout.Width(130f));
        GUILayout.Label(rec.duration.ToString("0.0") + "s", mutedStyle, GUILayout.Width(54f));
        GUILayout.Label(rec.frames.Count + " frames", mutedStyle, GUILayout.Width(92f));
        GUILayout.FlexibleSpace();
        GUI.enabled = CurrentMode == Mode.Standby;
        if (GUILayout.Button("Delete", buttonStyle, GUILayout.Width(72f), GUILayout.Height(26f)))
        {
            DeleteMemoryRecording(index);
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();
    }

    private void DrawSavedTab()
    {
        if (Time.unscaledTimeAsDouble >= nextSavedRefreshTime)
        {
            RefreshSavedTakes();
        }

        GUILayout.BeginVertical(panelStyle);
        GUILayout.Label("Save Current Take", titleStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Name", labelStyle, GUILayout.Width(48f));
        takeName = GUILayout.TextField(takeName, GUILayout.Height(28f));
        GUI.enabled = GetReadyCount() > 0;
        if (GUILayout.Button("Save", activeButtonStyle, GUILayout.Width(82f), GUILayout.Height(28f)))
        {
            SaveCurrentTake();
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("Takes folder: project/StudioRecordings", mutedStyle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Open folder", buttonStyle, GUILayout.Width(104f), GUILayout.Height(26f)))
        {
            OpenRecordingsFolder();
        }
        GUILayout.EndHorizontal();
        GUILayout.Label("Load previews a take in memory. Render creates a ready AVI video.", mutedStyle);
        GUILayout.EndVertical();

        GUILayout.Space(10f);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Saved Takes", titleStyle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Refresh", buttonStyle, GUILayout.Width(82f), GUILayout.Height(26f)))
        {
            RefreshSavedTakes();
        }
        GUILayout.EndHorizontal();

        savedScroll = GUILayout.BeginScrollView(savedScroll, GUILayout.Height(280f));
        if (savedTakePaths.Length == 0)
        {
            GUILayout.Label("No saved takes yet.", mutedStyle);
        }
        for (var i = 0; i < savedTakePaths.Length; i++)
        {
            DrawSavedTakeRow(savedTakePaths[i]);
        }
        GUILayout.EndScrollView();
    }

    private void DrawSavedTakeRow(string path)
    {
        GUILayout.BeginHorizontal(panelStyle);
        GUILayout.Label(Path.GetFileNameWithoutExtension(path), labelStyle);
        GUILayout.FlexibleSpace();
        GUI.enabled = CurrentMode == Mode.Standby;
        if (GUILayout.Button("Load", activeButtonStyle, GUILayout.Width(64f), GUILayout.Height(26f)))
        {
            LoadTake(path);
        }
        if (GUILayout.Button("Render", activeButtonStyle, GUILayout.Width(72f), GUILayout.Height(26f)))
        {
            RenderTake(path);
        }
        if (GUILayout.Button("Delete", buttonStyle, GUILayout.Width(72f), GUILayout.Height(26f)))
        {
            DeleteTake(path);
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();
    }

    private void DrawVideosTab()
    {
        if (Time.unscaledTimeAsDouble >= nextVideoRefreshTime)
        {
            RefreshVideoFiles();
        }

        GUILayout.BeginVertical(panelStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Rendered Videos", titleStyle);
        GUILayout.FlexibleSpace();
        GUILayout.Label(videoPaths.Length + " file(s)", mutedStyle, GUILayout.Width(72f));
        GUILayout.EndHorizontal();
        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Refresh", buttonStyle, GUILayout.Height(30f)))
        {
            RefreshVideoFiles();
        }
        if (GUILayout.Button("Open folder", buttonStyle, GUILayout.Height(30f)))
        {
            OpenVideosFolder();
        }
        GUILayout.EndHorizontal();
        GUILayout.Label("Folder: project/StudioVideos", mutedStyle);
        GUILayout.EndVertical();

        GUILayout.Space(10f);
        videoScroll = GUILayout.BeginScrollView(videoScroll, GUILayout.Height(342f));
        if (videoPaths.Length == 0)
        {
            GUILayout.Label("No rendered videos yet. Record a take, then press Render AVI video.", mutedStyle);
        }
        for (var i = 0; i < videoPaths.Length; i++)
        {
            DrawVideoRow(videoPaths[i]);
        }
        GUILayout.EndScrollView();
    }

    private void DrawVideoRow(string path)
    {
        var info = new FileInfo(path);
        GUILayout.BeginHorizontal(panelStyle);
        GUILayout.BeginVertical();
        GUILayout.Label(Path.GetFileName(path), labelStyle);
        GUILayout.Label(FormatFileSize(info.Exists ? info.Length : 0L) + "  " + (info.Exists ? info.LastWriteTime.ToString("yyyy-MM-dd HH:mm") : "missing"), mutedStyle);
        GUILayout.EndVertical();
        GUILayout.FlexibleSpace();
        GUI.enabled = info.Exists && !isRenderingVideo;
        if (GUILayout.Button("Open", activeButtonStyle, GUILayout.Width(64f), GUILayout.Height(28f)))
        {
            OpenFile(path);
        }
        if (GUILayout.Button("Delete", buttonStyle, GUILayout.Width(72f), GUILayout.Height(28f)))
        {
            DeleteVideo(path);
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();
    }

    private void DrawSettingsTab()
    {
        GUILayout.BeginVertical(panelStyle);
        GUILayout.Label("Video Export", titleStyle);
        GUILayout.Space(8f);
        videoFrameRate = Mathf.RoundToInt(DrawIntSlider("FPS", videoFrameRate, 10, 60));
        jpegQuality = Mathf.RoundToInt(DrawIntSlider("Quality", jpegQuality, 35, 100));
        GUILayout.Space(8f);
        GUILayout.Label("Higher quality makes bigger AVI files. If a player refuses AVI, use VLC or convert to MP4 later.", mutedStyle);
        GUILayout.EndVertical();

        GUILayout.Space(10f);
        GUILayout.BeginVertical(panelStyle);
        GUILayout.Label("Recording Workflow", titleStyle);
        GUILayout.Space(6f);
        var newGhostGuideEnabled = GUILayout.Toggle(ghostGuideEnabled, "Show transparent previous takes while recording");
        if (newGhostGuideEnabled != ghostGuideEnabled)
        {
            SetGhostGuideEnabled(newGhostGuideEnabled);
        }
        ghostGuideAlpha = DrawFloatSlider("Ghost alpha", ghostGuideAlpha, 0.05f, 0.8f);
        maxRecordingSeconds = DrawFloatSlider("Max take", maxRecordingSeconds, 30f, 600f);
        GUILayout.Label("R starts the current character's take. T stops only that take, so you can switch characters and record the next one.", mutedStyle);
        GUILayout.EndVertical();

        GUILayout.Space(10f);
        GUILayout.BeginVertical(panelStyle);
        GUILayout.Label("Output", titleStyle);
        GUILayout.Label("Recordings: project/StudioRecordings", mutedStyle);
        GUILayout.Label("Videos: project/StudioVideos", mutedStyle);
        GUILayout.Space(8f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Open recordings", buttonStyle, GUILayout.Height(30f)))
        {
            OpenRecordingsFolder();
        }
        if (GUILayout.Button("Open videos", buttonStyle, GUILayout.Height(30f)))
        {
            OpenVideosFolder();
        }
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }

    private void DrawHud()
    {
        var boxWidth = 292f;
        var boxHeight = CurrentMode == Mode.Recording ? 142f : 122f;
        var margin = 14f;
        var rect = new Rect(Screen.width - boxWidth - margin, margin, boxWidth, boxHeight);
        GUI.Box(rect, GUIContent.none, hudBoxStyle);
        GUILayout.BeginArea(new Rect(rect.x + 14f, rect.y + 10f, rect.width - 28f, rect.height - 20f));
        GUILayout.Label(GetModeLabel(), CurrentMode == Mode.Standby ? hudMutedStyle : hudAccentStyle);
        var active = FindActiveCharacter();
        var activeName = active != null ? CleanName(active.name) : "None";
        if (CurrentMode == Mode.Recording && RecordingTarget != null)
        {
            activeName = CleanName(RecordingTarget.name) + " (rec)";
        }
        GUILayout.Label("Control: " + activeName, hudLabelStyle);
        GUILayout.Label("Recorded: " + GetReadyCount() + " / " + maxRecordedCharacters + " ready", hudMutedStyle);
        GUILayout.Label("Guide: " + (ghostGuideEnabled ? "ghosts ON" : "ghosts OFF"), hudMutedStyle);
        if (CurrentMode == Mode.Recording)
        {
            GUILayout.Label("Take time: " + FormatDuration(Time.time - recordStartTime) + " / " + FormatDuration(maxRecordingSeconds), hudMutedStyle);
        }
        GUILayout.Label(isRenderingVideo ? "Rendering video..." : "R start | T stop | H guide | F9 video", hudMutedStyle);
        GUILayout.EndArea();
    }

    private void InitStyles()
    {
        if (windowStyle != null)
        {
            return;
        }

        var surface = new Color(0.07f, 0.08f, 0.09f, 0.98f);
        var panel = new Color(0.11f, 0.125f, 0.14f, 0.98f);
        var panelHover = new Color(0.15f, 0.17f, 0.19f, 1f);
        var accent = new Color(0.08f, 0.62f, 0.78f, 1f);

        windowStyle = new GUIStyle(GUI.skin.window);
        windowStyle.normal.background = MakeTex(2, 2, surface);
        windowStyle.normal.textColor = new Color(0.94f, 0.97f, 0.98f);
        windowStyle.fontSize = 17;
        windowStyle.fontStyle = FontStyle.Bold;
        windowStyle.padding = new RectOffset(14, 14, 24, 14);

        panelStyle = new GUIStyle(GUI.skin.box);
        panelStyle.normal.background = MakeTex(2, 2, panel);
        panelStyle.padding = new RectOffset(10, 10, 8, 8);

        buttonStyle = new GUIStyle(GUI.skin.button);
        buttonStyle.normal.background = MakeTex(2, 2, new Color(0.16f, 0.18f, 0.2f, 1f));
        buttonStyle.hover.background = MakeTex(2, 2, panelHover);
        buttonStyle.normal.textColor = new Color(0.88f, 0.92f, 0.94f);
        buttonStyle.fontStyle = FontStyle.Bold;

        activeButtonStyle = new GUIStyle(buttonStyle);
        activeButtonStyle.normal.background = MakeTex(2, 2, accent);
        activeButtonStyle.normal.textColor = Color.white;

        tabStyle = new GUIStyle(buttonStyle);
        tabStyle.normal.background = MakeTex(2, 2, new Color(0.10f, 0.115f, 0.13f, 1f));
        activeTabStyle = new GUIStyle(activeButtonStyle);

        labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.normal.textColor = new Color(0.90f, 0.94f, 0.96f);
        labelStyle.fontStyle = FontStyle.Bold;

        mutedStyle = new GUIStyle(labelStyle);
        mutedStyle.normal.textColor = new Color(0.62f, 0.68f, 0.72f);
        mutedStyle.fontStyle = FontStyle.Normal;
        mutedStyle.fontSize = 12;

        titleStyle = new GUIStyle(labelStyle);
        titleStyle.fontSize = 14;

        hudBoxStyle = new GUIStyle(GUI.skin.box);
        hudBoxStyle.normal.background = MakeTex(2, 2, new Color(0.06f, 0.07f, 0.08f, 0.88f));
        hudLabelStyle = new GUIStyle(labelStyle);
        hudLabelStyle.fontSize = 13;
        hudAccentStyle = new GUIStyle(hudLabelStyle);
        hudAccentStyle.normal.textColor = new Color(1f, 0.35f, 0.3f);
        hudMutedStyle = new GUIStyle(mutedStyle);
    }

    private static void SetMenuOpen(bool open)
    {
        IsMenuOpen = open;
        if (open)
        {
            if (Instance != null)
            {
                Instance.RefreshSavedTakes();
                Instance.RefreshVideoFiles();
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    private void SetStatus(string message)
    {
        statusMessage = message;
        Debug.Log("[Recorder] " + message);
    }

    private string GetModeLabel()
    {
        return CurrentMode switch
        {
            Mode.Recording => "RECORDING",
            Mode.Playing => "PLAYING",
            Mode.Rendering => "RENDERING VIDEO",
            _ => "STANDBY"
        };
    }

    private string FormatDuration(float seconds)
    {
        seconds = Mathf.Max(0f, seconds);
        var minutes = Mathf.FloorToInt(seconds / 60f);
        var secs = Mathf.FloorToInt(seconds % 60f);
        return minutes + ":" + secs.ToString("00");
    }

    private string GetRecordingsDirectory()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "StudioRecordings"));
    }

    private string GetVideosDirectory()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "StudioVideos"));
    }

    private void EnsureOutputFolders()
    {
        Directory.CreateDirectory(GetRecordingsDirectory());
        Directory.CreateDirectory(GetVideosDirectory());
    }

    private void OpenRecordingsFolder()
    {
        OpenFolder(GetRecordingsDirectory(), "Opened recordings folder.");
    }

    private void OpenVideosFolder()
    {
        OpenFolder(GetVideosDirectory(), "Opened videos folder.");
    }

    private void OpenFolder(string directory, string successMessage)
    {
        Directory.CreateDirectory(directory);

        try
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            System.Diagnostics.Process.Start("explorer.exe", directory);
#else
            Application.OpenURL("file:///" + directory.Replace("\\", "/"));
#endif
            SetStatus(successMessage);
        }
        catch (Exception ex)
        {
            Application.OpenURL("file:///" + directory.Replace("\\", "/"));
            SetStatus("Opened folder fallback: " + ex.Message);
        }
    }

    private void OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            RefreshVideoFiles();
            SetStatus("Video file not found.");
            return;
        }

        try
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
#else
            Application.OpenURL("file:///" + path.Replace("\\", "/"));
#endif
            SetStatus("Opened video: " + Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            Application.OpenURL("file:///" + path.Replace("\\", "/"));
            SetStatus("Opened video fallback: " + ex.Message);
        }
    }

    private float DrawIntSlider(string label, int value, int min, int max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, labelStyle, GUILayout.Width(72f));
        var result = GUILayout.HorizontalSlider(value, min, max);
        GUILayout.Label(Mathf.RoundToInt(result).ToString(), mutedStyle, GUILayout.Width(44f));
        GUILayout.EndHorizontal();
        return result;
    }

    private float DrawFloatSlider(string label, float value, float min, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, labelStyle, GUILayout.Width(92f));
        var result = GUILayout.HorizontalSlider(value, min, max);
        GUILayout.Label(result.ToString("0.00"), mutedStyle, GUILayout.Width(44f));
        GUILayout.EndHorizontal();
        return result;
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Take";
        }

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '_');
        }
        return value.Trim();
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L)
        {
            return (bytes / (1024f * 1024f * 1024f)).ToString("0.0") + " GB";
        }
        if (bytes >= 1024L * 1024L)
        {
            return (bytes / (1024f * 1024f)).ToString("0.0") + " MB";
        }
        if (bytes >= 1024L)
        {
            return (bytes / 1024f).ToString("0.0") + " KB";
        }
        return bytes + " B";
    }

    private static float GetAnimFloat(Animator anim, int id)
    {
        return anim != null ? anim.GetFloat(id) : 0f;
    }

    private static bool GetAnimBool(Animator anim, int id)
    {
        return anim != null && anim.GetBool(id);
    }

    private static Texture2D MakeTex(int w, int h, Color col)
    {
        var pix = new Color[w * h];
        for (var i = 0; i < pix.Length; i++) pix[i] = col;
        var tex = new Texture2D(w, h);
        tex.SetPixels(pix);
        tex.Apply();
        return tex;
    }

    private static string CleanName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Unknown";
        var idx = name.IndexOf("(Clone)", StringComparison.Ordinal);
        if (idx >= 0) name = name.Substring(0, idx).TrimEnd();
        idx = name.IndexOf("_Spawned_", StringComparison.Ordinal);
        if (idx >= 0) name = name.Substring(0, idx).TrimEnd();
        return name.Trim();
    }

    private sealed class SimpleAviWriter : IDisposable
    {
        private readonly FileStream stream;
        private readonly BinaryWriter writer;
        private readonly List<FrameIndex> index = new();
        private readonly int width;
        private readonly int height;
        private readonly int frameRate;
        private readonly long riffSizePosition;
        private readonly long totalFramesPosition;
        private readonly long avihSuggestedBufferPosition;
        private readonly long streamLengthPosition;
        private readonly long streamSuggestedBufferPosition;
        private readonly long moviListSizePosition;
        private readonly long moviDataStart;
        private int maxFrameSize;
        private bool disposed;

        public SimpleAviWriter(string path, int width, int height, int frameRate)
        {
            this.width = Mathf.Max(2, width);
            this.height = Mathf.Max(2, height);
            this.frameRate = Mathf.Max(1, frameRate);
            stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            writer = new BinaryWriter(stream);

            WriteFourCC("RIFF");
            riffSizePosition = stream.Position;
            writer.Write(0);
            WriteFourCC("AVI ");

            var hdrlSizePosition = BeginList("hdrl");
            WriteAvih(out totalFramesPosition, out avihSuggestedBufferPosition);

            var strlSizePosition = BeginList("strl");
            WriteStrh(out streamLengthPosition, out streamSuggestedBufferPosition);
            WriteStrf();
            EndList(strlSizePosition);
            EndList(hdrlSizePosition);

            moviListSizePosition = BeginList("movi");
            moviDataStart = stream.Position;
        }

        public void AddFrame(byte[] jpegBytes)
        {
            if (disposed || jpegBytes == null || jpegBytes.Length == 0)
            {
                return;
            }

            var chunkStart = stream.Position;
            WriteFourCC("00dc");
            writer.Write(jpegBytes.Length);
            writer.Write(jpegBytes);
            if ((jpegBytes.Length & 1) != 0)
            {
                writer.Write((byte)0);
            }

            index.Add(new FrameIndex
            {
                offset = (int)(chunkStart - moviDataStart),
                size = jpegBytes.Length
            });
            maxFrameSize = Mathf.Max(maxFrameSize, jpegBytes.Length);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            var idxStart = stream.Position;
            WriteFourCC("idx1");
            writer.Write(index.Count * 16);
            for (var i = 0; i < index.Count; i++)
            {
                WriteFourCC("00dc");
                writer.Write(0x10);
                writer.Write(index[i].offset);
                writer.Write(index[i].size);
            }

            var fileEnd = stream.Position;
            PatchInt(riffSizePosition, (int)(fileEnd - 8));
            PatchInt(totalFramesPosition, index.Count);
            PatchInt(avihSuggestedBufferPosition, maxFrameSize);
            PatchInt(streamLengthPosition, index.Count);
            PatchInt(streamSuggestedBufferPosition, maxFrameSize);
            PatchInt(moviListSizePosition, (int)(idxStart - moviListSizePosition - 4));

            writer.Dispose();
            stream.Dispose();
        }

        private void WriteAvih(out long framesPosition, out long suggestedBufferPosition)
        {
            WriteFourCC("avih");
            writer.Write(56);
            writer.Write(1000000 / frameRate);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0x10);
            framesPosition = stream.Position;
            writer.Write(0);
            writer.Write(0);
            writer.Write(1);
            suggestedBufferPosition = stream.Position;
            writer.Write(0);
            writer.Write(width);
            writer.Write(height);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
        }

        private void WriteStrh(out long lengthPosition, out long suggestedBufferPosition)
        {
            WriteFourCC("strh");
            writer.Write(56);
            WriteFourCC("vids");
            WriteFourCC("MJPG");
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(1);
            writer.Write(frameRate);
            writer.Write(0);
            lengthPosition = stream.Position;
            writer.Write(0);
            suggestedBufferPosition = stream.Position;
            writer.Write(0);
            writer.Write(-1);
            writer.Write(0);
            writer.Write((short)0);
            writer.Write((short)0);
            writer.Write((short)width);
            writer.Write((short)height);
        }

        private void WriteStrf()
        {
            WriteFourCC("strf");
            writer.Write(40);
            writer.Write(40);
            writer.Write(width);
            writer.Write(height);
            writer.Write((short)1);
            writer.Write((short)24);
            WriteFourCC("MJPG");
            writer.Write(width * height * 3);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
        }

        private long BeginList(string fourCc)
        {
            WriteFourCC("LIST");
            var sizePosition = stream.Position;
            writer.Write(0);
            WriteFourCC(fourCc);
            return sizePosition;
        }

        private void EndList(long sizePosition)
        {
            var current = stream.Position;
            PatchInt(sizePosition, (int)(current - sizePosition - 4));
            stream.Position = current;
        }

        private void PatchInt(long position, int value)
        {
            var current = stream.Position;
            stream.Position = position;
            writer.Write(value);
            stream.Position = current;
        }

        private void WriteFourCC(string value)
        {
            writer.Write((byte)value[0]);
            writer.Write((byte)value[1]);
            writer.Write((byte)value[2]);
            writer.Write((byte)value[3]);
        }

        private struct FrameIndex
        {
            public int offset;
            public int size;
        }
    }
}
